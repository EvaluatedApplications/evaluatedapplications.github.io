/*
 * fem-solver.js: sparse direct solver for finite element systems, plain JavaScript (ES module, no dependencies, no WASM, no build step).
 *
 * Copyright (c) Evaluated Applications. A gift to the StructArk team: use it, change it and ship it in StructArk, with no payment and no licence key.
 *
 * What it does
 *   Solves K x = f for a symmetric positive definite stiffness matrix K assembled from element matrices, with some dofs held (fixed at zero).
 *   1. Held dofs are removed before anything else: the system is built on the free dofs only.
 *   2. Ordering: nested dissection on the NODE graph (a node = `blockSize` consecutive dofs, 6 for a 3D frame or shell node).
 *      With node coordinates it cuts the longest side of the bounding box at the median and takes the thinner boundary layer as the separator;
 *      without coordinates it cuts a breadth-first level structure in the middle. Leaves of at most `leaf` nodes become one front.
 *   3. Multifrontal Cholesky: every separator of the dissection tree is one dense front; a front is assembled from the matrix entries of its pivots and
 *      the update matrices of its children, partially factored with a blocked dense kernel, and its Schur complement is passed to its parent.
 *   4. Solves: forward and backward substitution over the same tree. Any number of load cases reuse the held factor.
 *   5. The held factor can also be edited in place (rank-one Cholesky updates and downdates along the tree path): see FemSession at the end.
 *
 * Typical use
 *   const s = new FemSolver({ nDofs, blockSize: 6, held: [0, 1, 2], coords });   // coords: Float64Array of 3 numbers per node, optional
 *   for (const e of elements) s.connect(e.dofs);                                  // pattern: once per element
 *   s.analyze();                                                                  // ordering + symbolic analysis
 *   for (const e of elements) s.addElement(e.dofs, e.ke);                         // values: full symmetric element matrix, row-major
 *   s.factor();
 *   const x = s.solve(f);                                                         // f, x: Float64Array(nDofs); held entries of x are 0
 *   const r = s.residual(x, f);                                                   // ||K x - f|| / ||f|| on the free dofs
 *   const x2 = s.solve(f2);                                                       // another load case: no refactoring
 *   // from triplets: FemSolver.fromTriplets({ nDofs, rows, cols, vals, held, blockSize, coords })   (both triangles of K, duplicates are summed)
 *
 * Running it without blocking the page
 *   Nothing here touches the DOM or Node, so run the whole thing inside a Worker and post the results back:
 *     // worker.js (type: 'module')
 *     import { FemSolver } from './fem-solver.js';
 *     onmessage = ({ data: { nDofs, rows, cols, vals, held, blockSize, coords, f } }) => {
 *       const s = FemSolver.fromTriplets({ nDofs, rows, cols, vals, held, blockSize, coords });
 *       s.factor(); const x = s.solve(f);
 *       postMessage({ x, residual: s.residual(x, f) }, [x.buffer]);
 *     };
 *     // page: new Worker('./worker.js', { type: 'module' }) (serve over http or https; module workers do not start from file://)
 *
 * Editing a model and solving again (FemSession, at the end of this file)
 *   After the first factor, a change to a few elements does not need a new factorisation: the held factor is updated in place along the path of the
 *   changed elements through the elimination tree, so the cost follows the size of the edit. If a downdate fails, or the relative residual passes 1e-9,
 *   the session refactors by itself and says so. For large edits (about 130 elements at 100k unknowns, about 55 at 10k) a fresh solve is cheaper.
 *
 * Measured (headless Edge, one Worker, one thread, median of 3, a plate of quads with 2 dofs per node; your laptop will differ)
 *   Full solve, time to the first answer:  2.6k unknowns 27 ms | 10k 107 ms | 25k 474 ms | 39k 839 ms | 100k 3.1 s | 242k 12.5 s | 396k 30 s (1.3 GB in Node).
 *   Edit of k stiffer quads on the held factor, against a fresh full solve:  10k: 6 / 20 / 185 ms for k = 1 / 10 / 100 against 108 ms;
 *   100k: 66 / 269 / 2,267 ms against 2,960 ms. Another load case on the held factor: 3 ms at 10k, 44 ms at 100k.
 *   Accuracy: relative residual 2e-13 or better after a full solve; 2.3e-10 after 100 successive edits at 100k, with no refactor needed.
 *   The main thread stays free: 4 to 6 ms between ticks while the worker solves.
 *
 * Limits
 *   One thread. The dissection tree is built for several (`taskRoots()`): the independent subtrees can be factored by different workers over
 *   SharedArrayBuffers, which needs a page served with Cross-Origin-Opener-Policy: same-origin and Cross-Origin-Embedder-Policy: require-corp
 *   (a host that lets you set headers; a threaded version is a natural next step). Memory is the cap, about 3 KB per unknown on these plates.
 *   Held dofs are held at zero. The matrix must be symmetric positive definite; a singular system throws.
 */
'use strict';

const now = () => (typeof performance !== 'undefined' ? performance.now() : Date.now());

export class FemSolver {
  /**
   * @param {object} o
   * @param {number} o.nDofs        number of dofs, held ones included
   * @param {number} [o.blockSize]  dofs per node (nodes group consecutive dofs); 1 for a plain matrix
   * @param {ArrayLike<number>} [o.held]  dofs that are held at zero
   * @param {Float64Array} [o.coords]     3 coordinates per node (enables geometric nested dissection)
   * @param {number} [o.leaf]       nested dissection stops at this many nodes (default 6)
   */
  constructor({ nDofs, blockSize = 1, held = [], coords = null, leaf = 6 }) {
    this.nDofs = nDofs; this.bs = blockSize; this.leaf = leaf; this.coords = coords;
    this.nNodes = Math.ceil(nDofs / blockSize);
    const heldMask = new Uint8Array(nDofs); for (const d of held) heldMask[d] = 1;
    this.heldMask = heldMask;
    const freeOf = new Int32Array(nDofs); let nf = 0;
    for (let d = 0; d < nDofs; d++) freeOf[d] = heldMask[d] ? -1 : nf++;
    this.freeOf = freeOf; this.nFree = nf;
    this.edgeA = []; this.edgeB = [];        // node pairs from connect()
    this.analyzed = false; this.factored = false;
    this.timing = {};
  }

  static fromTriplets({ nDofs, rows, cols, vals, held = [], blockSize = 1, coords = null, leaf = 6 }) {
    const s = new FemSolver({ nDofs, blockSize, held, coords, leaf });
    const bs = blockSize, pair = new Int32Array(2);
    for (let k = 0; k < rows.length; k++) { const a = (rows[k] / bs) | 0, b = (cols[k] / bs) | 0; if (a !== b) { s.edgeA.push(a); s.edgeB.push(b); } }
    s.analyze();
    for (let k = 0; k < rows.length; k++) s.addEntry(rows[k], cols[k], vals[k]);
    return s;
  }

  /** Declare that the dofs of one element are coupled. Call once per element before analyze(). */
  connect(dofs) {
    const bs = this.bs, n = dofs.length; const nodes = [];
    for (let i = 0; i < n; i++) { const nd = (dofs[i] / bs) | 0; if (!nodes.includes(nd)) nodes.push(nd); }
    for (let i = 0; i < nodes.length; i++) for (let j = i + 1; j < nodes.length; j++) { this.edgeA.push(nodes[i]); this.edgeB.push(nodes[j]); }
  }

  // ------------------------------------------------------------------------------------------------ analysis
  /** Ordering and symbolic analysis. Returns the statistics object. */
  analyze() {
    const t0 = now(); const nn = this.nNodes, bs = this.bs, freeOf = this.freeOf;
    // free dofs of each node
    const nfStart = new Int32Array(nn + 1), nfDof = new Int32Array(this.nFree);
    { let q = 0; for (let a = 0; a < nn; a++) { nfStart[a] = q; for (let d = a * bs; d < Math.min(this.nDofs, (a + 1) * bs); d++) if (freeOf[d] >= 0) nfDof[q++] = freeOf[d]; } nfStart[nn] = q; }
    const hasFree = (a) => nfStart[a + 1] > nfStart[a];
    // adjacency (CSR, duplicates removed, nodes without free dofs left out)
    const ea = this.edgeA, eb = this.edgeB; const deg = new Int32Array(nn + 1);
    for (let k = 0; k < ea.length; k++) if (hasFree(ea[k]) && hasFree(eb[k])) { deg[ea[k] + 1]++; deg[eb[k] + 1]++; }
    for (let a = 0; a < nn; a++) deg[a + 1] += deg[a];
    const adjRaw = new Int32Array(deg[nn]), fill = deg.slice(0, nn);
    for (let k = 0; k < ea.length; k++) if (hasFree(ea[k]) && hasFree(eb[k])) { adjRaw[fill[ea[k]]++] = eb[k]; adjRaw[fill[eb[k]]++] = ea[k]; }
    const adjStart = new Int32Array(nn + 1), mark = new Int32Array(nn).fill(-1); const adjTmp = [];
    for (let a = 0; a < nn; a++) {
      adjStart[a] = adjTmp.length;
      for (let q = deg[a]; q < deg[a + 1]; q++) { const b = adjRaw[q]; if (mark[b] !== a) { mark[b] = a; adjTmp.push(b); } }
    }
    adjStart[nn] = adjTmp.length; const adj = Int32Array.from(adjTmp);
    this.edgeA = []; this.edgeB = [];
    // nested dissection tree
    const tree = nestedDissection(nn, nfStart, adjStart, adj, this.coords, this.leaf);
    const nt = tree.pivots.length;
    // permutation
    const perm = new Int32Array(this.nFree), inv = new Int32Array(this.nFree);
    const treeOf = new Int32Array(nn).fill(-1), ordPos = new Int32Array(nn).fill(-1), nodeStart = new Int32Array(nn).fill(-1);
    const tPivStart = new Int32Array(nt), tP = new Int32Array(nt), tParent = new Int32Array(nt).fill(-1);
    { let next = 0, pos = 0;
      for (let t = 0; t < nt; t++) {
        tPivStart[t] = next;
        for (const nd of tree.pivots[t]) {
          treeOf[nd] = t; ordPos[nd] = pos++; nodeStart[nd] = next;
          for (let q = nfStart[nd]; q < nfStart[nd + 1]; q++) { perm[nfDof[q]] = next; inv[next] = nfDof[q]; next++; }
        }
        tP[t] = next - tPivStart[t];
      }
      for (let t = 0; t < nt; t++) for (const c of tree.children[t]) tParent[c] = t;
    }
    // borders: the nodes of ancestors that touch the subtree
    const tBorder = new Array(nt), tBorderNodes = new Array(nt), stamp = new Int32Array(nn);
    for (let t = 0; t < nt; t++) {
      const list = [], st = t + 1;
      for (const v of tree.pivots[t]) for (let q = adjStart[v]; q < adjStart[v + 1]; q++) { const u = adj[q]; if (treeOf[u] > t && stamp[u] !== st) { stamp[u] = st; list.push(u); } }
      for (const c of tree.children[t]) for (const u of tBorderNodes[c]) if (treeOf[u] > t && stamp[u] !== st) { stamp[u] = st; list.push(u); }
      list.sort((a, b) => nodeStart[a] - nodeStart[b]);
      tBorderNodes[t] = list;
      let cnt = 0; for (const u of list) cnt += nfStart[u + 1] - nfStart[u];
      const bd = new Int32Array(cnt); let q = 0;
      for (const u of list) for (let k = 0; k < nfStart[u + 1] - nfStart[u]; k++) bd[q++] = nodeStart[u] + k;
      tBorder[t] = bd;
      for (const c of tree.children[t]) tBorderNodes[c] = null;
    }
    // lower triangle of K, column compressed: column of dof q of node a holds the rest of a's dofs, then the dofs of later ordered neighbours
    const n = this.nFree, Ap = new Int32Array(n + 1);
    const nbr = new Array(nn);
    for (let a = 0; a < nn; a++) {
      if (ordPos[a] < 0) continue;
      const l = []; for (let q = adjStart[a]; q < adjStart[a + 1]; q++) if (ordPos[adj[q]] > ordPos[a]) l.push(adj[q]);
      l.sort((x, y) => ordPos[x] - ordPos[y]); nbr[a] = l;
      let rows = 0; for (const b of l) rows += nfStart[b + 1] - nfStart[b];
      const cA = nfStart[a + 1] - nfStart[a];
      for (let q = 0; q < cA; q++) Ap[nodeStart[a] + q + 1] = (cA - q) + rows;
    }
    for (let i = 0; i < n; i++) Ap[i + 1] += Ap[i];
    const Ai = new Int32Array(Ap[n]), Ax = new Float64Array(Ap[n]);
    for (let a = 0; a < nn; a++) {
      if (ordPos[a] < 0) continue; const cA = nfStart[a + 1] - nfStart[a];
      for (let q = 0; q < cA; q++) {
        let p = Ap[nodeStart[a] + q];
        for (let r = q; r < cA; r++) Ai[p++] = nodeStart[a] + r;
        for (const b of nbr[a]) for (let k = 0; k < nfStart[b + 1] - nfStart[b]; k++) Ai[p++] = nodeStart[b] + k;
      }
    }
    // front sizes, work, task roots (independent subtrees, for threaded use)
    const tWork = new Float64Array(nt), tSub = new Float64Array(nt), tFirst = new Int32Array(nt);
    let nnzL = 0, flops = 0, maxM = 0, maxB = 0;
    for (let t = 0; t < nt; t++) {
      const p = tP[t], b = tBorder[t].length, m = p + b;
      tWork[t] = p * p * p / 3 + p * p * b + p * b * b; tSub[t] = tWork[t]; tFirst[t] = t;
      for (const c of tree.children[t]) tSub[t] += tSub[c];
      if (tree.children[t].length) tFirst[t] = tFirst[tree.children[t][0]];
      nnzL += p * (p + 1) / 2 + b * p; flops += 2 * tWork[t]; maxM = Math.max(maxM, m); maxB = Math.max(maxB, b);
    }
    Object.assign(this, { nt, perm, inv, tPivStart, tP, tParent, tBorder, tChildren: tree.children, Ap, Ai, Ax, tWork, tSub, tFirst, nnzL, flops, maxM, maxB });
    this.frontOfCol = new Int32Array(n); for (let t = 0; t < nt; t++) for (let c = 0; c < tP[t]; c++) this.frontOfCol[tPivStart[t] + c] = t;
    this.loc = new Int32Array(n); this.rel = new Int32Array(Math.max(1, maxB));
    this.analyzed = true; this.timing.analyze = now() - t0;
    return this.stats();
  }

  /** Roots of the independent subtrees whose work is below total/(4*workers); the fronts above them form the shared top of the tree. */
  taskRoots(workers = 8) {
    const total = this.tWork.reduce((a, b) => a + b, 0), thr = total / (4 * workers), out = [];
    for (let t = 0; t < this.nt; t++) if (this.tSub[t] <= thr && (this.tParent[t] < 0 || this.tSub[this.tParent[t]] > thr)) out.push(t);
    return out.sort((a, b) => this.tSub[b] - this.tSub[a]);
  }

  stats() {
    return { nDofs: this.nDofs, nFree: this.nFree, fronts: this.nt, maxFront: this.maxM, nnzL: this.nnzL, factorMB: this.nnzL * 8 / 1048576, gflop: this.flops / 1e9, nnzK: this.Ap ? this.Ap[this.nFree] : 0 };
  }

  // ------------------------------------------------------------------------------------------------ assembly
  /** Zero the matrix values (pattern and ordering stay). */
  clear() { this.Ax.fill(0); this.factored = false; }

  _find(col, row) {
    const Ai = this.Ai; let lo = this.Ap[col], hi = this.Ap[col + 1] - 1;
    while (lo <= hi) { const mid = (lo + hi) >> 1, r = Ai[mid]; if (r === row) return mid; if (r < row) lo = mid + 1; else hi = mid - 1; }
    throw new Error('entry outside the sparsity pattern: connect() the dofs of this element before analyze()');
  }

  /** Add one entry K[i][j] (global dofs). Entries of the upper triangle are ignored, so give a symmetric matrix as both triangles or only the lower. */
  addEntry(i, j, v) {
    const fi = this.freeOf[i], fj = this.freeOf[j]; if (fi < 0 || fj < 0) return;
    const pi = this.perm[fi], pj = this.perm[fj]; if (pi < pj) return;
    this.Ax[this._find(pj, pi)] += v;
  }

  /** Add a full symmetric element matrix (row-major m x m) for the given global dofs. */
  addElement(dofs, ke) {
    const m = dofs.length, perm = this.perm, freeOf = this.freeOf, Ax = this.Ax;
    const pd = this._pd && this._pd.length >= m ? this._pd : (this._pd = new Int32Array(Math.max(m, 32)));
    for (let i = 0; i < m; i++) { const f = freeOf[dofs[i]]; pd[i] = f < 0 ? -1 : perm[f]; }
    for (let j = 0; j < m; j++) {
      const pj = pd[j]; if (pj < 0) continue;
      for (let i = 0; i < m; i++) { const pi = pd[i]; if (pi < pj) continue; Ax[this._find(pj, pi)] += ke[i * m + j]; }
    }
    this.factored = false;
  }

  // ------------------------------------------------------------------------------------------------ factorisation
  /** Numeric factorisation of the assembled matrix. Throws if the matrix is not positive definite (a mechanism). */
  factor() {
    if (!this.analyzed) throw new Error('call analyze() first');
    const t0 = now(); const nt = this.nt;
    const { tPivStart, tP, tBorder, tChildren, Ap, Ai, Ax, loc } = this;
    if (!this.L) { this.L = new Array(nt); this.Y = new Array(nt); }
    const U = new Array(nt);
    let F = this._F && this._F.length >= this.maxM * this.maxM ? this._F : (this._F = new Float64Array(this.maxM * this.maxM));
    const panel = this._panel && this._panel.length >= this.maxM * 64 ? this._panel : (this._panel = new Float64Array(this.maxM * 64));
    const invd = this._invd || (this._invd = new Float64Array(64));
    const rel = this.rel;
    for (let t = 0; t < nt; t++) {
      const p = tP[t], ps = tPivStart[t], pe = ps + p, border = tBorder[t], bd = border.length, m = p + bd;
      F.fill(0, 0, m * m);
      for (let i = 0; i < bd; i++) loc[border[i]] = p + i;
      for (let c = 0; c < p; c++) {
        const col = ps + c;
        for (let q = Ap[col], e = Ap[col + 1]; q < e; q++) { const r = Ai[q]; F[(r < pe ? r - ps : loc[r]) * m + c] += Ax[q]; }
      }
      const kids = tChildren[t];
      for (let k = 0; k < kids.length; k++) {
        const ch = kids[k], cb = tBorder[ch], bc = cb.length; if (bc === 0) continue;
        const Uc = U[ch];
        for (let i = 0; i < bc; i++) { const d = cb[i]; rel[i] = d < pe ? d - ps : loc[d]; }
        for (let i = 0, ub = 0; i < bc; i++, ub += i) { const rb = rel[i] * m; for (let j = 0; j <= i; j++) F[rb + rel[j]] += Uc[ub + j]; }
        U[ch] = null;
      }
      const bad = partialCholesky(F, m, p, panel, invd);
      if (bad >= 0) { this.factored = false; throw new Error('matrix is not positive definite near dof ' + this._globalDof(ps + bad) + ' (the structure may be a mechanism)'); }
      // keep the first p columns as the factor panel (m x p, row-major) and pass the Schur complement up
      let Lp = this.L[t]; if (!Lp || Lp.length !== m * p) Lp = this.L[t] = new Float64Array(m * p);
      for (let i = 0; i < m; i++) { const n = i < p ? i + 1 : p, src = i * m, dst = i * p; for (let j = 0; j < n; j++) Lp[dst + j] = F[src + j]; }
      if (bd > 0) {
        const Ut = new Float64Array(bd * (bd + 1) / 2); let off = 0;
        for (let i = 0; i < bd; i++) { const src = (p + i) * m + p; for (let j = 0; j <= i; j++) Ut[off + j] = F[src + j]; off += i + 1; }
        U[t] = Ut;
        if (!this.Y[t] || this.Y[t].length !== bd) this.Y[t] = new Float64Array(bd);
      }
    }
    this.factored = true; this.timing.factor = now() - t0;
    return this.timing.factor;
  }

  _globalDof(permIndex) { const f = this.inv[permIndex]; for (let d = 0; d < this.nDofs; d++) if (this.freeOf[d] === f) return d; return -1; }

  // ------------------------------------------------------------------------------------------------ solves
  /** Solve K x = f on the free dofs. f and the result are indexed by global dof; held dofs of the result are 0. */
  solve(f, out) {
    if (!this.factored) throw new Error('call factor() first');
    const t0 = now(); const n = this.nFree, w = this._w || (this._w = new Float64Array(n)), inv = this.inv, freeOf = this.freeOf;
    // permuted right-hand side
    const gOfFree = this._gOfFree || (this._gOfFree = (() => { const g = new Int32Array(n); for (let d = 0; d < this.nDofs; d++) if (freeOf[d] >= 0) g[freeOf[d]] = d; return g; })());
    for (let i = 0; i < n; i++) w[i] = f[gOfFree[inv[i]]];
    const { tPivStart, tP, tBorder, tChildren, L, Y, loc } = this; const nt = this.nt;
    let vec = this._vec && this._vec.length >= this.maxM ? this._vec : (this._vec = new Float64Array(this.maxM));
    for (let t = 0; t < nt; t++) {                     // forward: children before parents
      const p = tP[t], ps = tPivStart[t], pe = ps + p, border = tBorder[t], bd = border.length, Lt = L[t];
      for (let i = 0; i < p; i++) vec[i] = w[ps + i];
      for (let i = 0; i < bd; i++) { vec[p + i] = 0; loc[border[i]] = p + i; }
      const kids = tChildren[t];
      for (let k = 0; k < kids.length; k++) {
        const ch = kids[k], cb = tBorder[ch]; if (cb.length === 0) continue; const y = Y[ch];
        for (let i = 0; i < cb.length; i++) { const d = cb[i]; vec[d < pe ? d - ps : loc[d]] += y[i]; }
      }
      for (let i = 0; i < p; i++) { let s = vec[i]; const row = i * p; for (let k = 0; k < i; k++) s -= Lt[row + k] * vec[k]; vec[i] = s / Lt[row + i]; }
      for (let i = 0; i < p; i++) w[ps + i] = vec[i];
      if (bd > 0) { const y = Y[t]; for (let i = 0; i < bd; i++) { let s = vec[p + i]; const row = (p + i) * p; for (let k = 0; k < p; k++) s -= Lt[row + k] * vec[k]; y[i] = s; } }
    }
    for (let t = nt - 1; t >= 0; t--) {                // backward: parents before children
      const p = tP[t], ps = tPivStart[t], border = tBorder[t], bd = border.length, Lt = L[t];
      for (let i = 0; i < p; i++) vec[i] = w[ps + i];
      for (let i = 0; i < bd; i++) { const xb = w[border[i]]; if (xb !== 0) { const row = (p + i) * p; for (let k = 0; k < p; k++) vec[k] -= xb * Lt[row + k]; } }
      for (let j = p - 1; j >= 0; j--) { const row = j * p, xj = vec[j] / Lt[row + j]; vec[j] = xj; for (let k = 0; k < j; k++) vec[k] -= xj * Lt[row + k]; }
      for (let i = 0; i < p; i++) w[ps + i] = vec[i];
    }
    const x = out || new Float64Array(this.nDofs); x.fill(0);
    for (let i = 0; i < n; i++) x[gOfFree[inv[i]]] = w[i];
    this.timing.solve = now() - t0;
    return x;
  }

  /** Relative residual ||K x - f|| / ||f|| on the free dofs, computed from the assembled matrix. */
  residual(x, f) {
    const n = this.nFree, { Ap, Ai, Ax, perm, freeOf } = this; const y = new Float64Array(n), xp = new Float64Array(n), fp = new Float64Array(n);
    for (let d = 0; d < this.nDofs; d++) { const fr = freeOf[d]; if (fr >= 0) { xp[perm[fr]] = x[d]; fp[perm[fr]] = f[d]; } }
    for (let c = 0; c < n; c++) for (let q = Ap[c]; q < Ap[c + 1]; q++) { const r = Ai[q], a = Ax[q]; y[r] += a * xp[c]; if (r !== c) y[c] += a * xp[r]; }
    let nr = 0, nf = 0; for (let i = 0; i < n; i++) { const d = y[i] - fp[i]; nr += d * d; nf += fp[i] * fp[i]; }
    return nf === 0 ? Math.sqrt(nr) : Math.sqrt(nr / nf);
  }
}

// ==================================================================================================== held-factor edits
/**
 * Edit the held factor in place. A change of one element stiffness is a symmetric semidefinite matrix dK on the dofs of the element (stiffer: positive,
 * softer: negative). It is written as a sum of rank-one terms w w^T by a pivoted Cholesky factorisation (at most one per element dof), and each term is applied
 * to the factor as a Cholesky update (stiffer) or downdate (softer). A term only touches the fronts on the path from the element up to the root of the tree,
 * so the cost follows the size of the edit and not the size of the model. All terms of one element lie inside that element's dofs, which are coupled in the
 * matrix, so the factor never needs a new entry.
 */
FemSolver.prototype.updateRank = function (dofs, dKe) {
  if (!this.factored) throw new Error('call factor() first');
  const m = dofs.length, freeOf = this.freeOf, perm = this.perm;
  let tr = 0; for (let i = 0; i < m; i++) tr += dKe[i * m + i];
  const sign = tr >= 0 ? 1 : -1;
  this.addElement(dofs, dKe); this.factored = true;               // keep the assembled matrix equal to what the factor represents
  // restrict to the free dofs and take the pivoted Cholesky of sign * dK
  const loc = []; for (let i = 0; i < m; i++) if (freeOf[dofs[i]] >= 0) loc.push(i);
  const d = loc.length; if (d === 0) return true;
  const C = new Float64Array(d * d); for (let a = 0; a < d; a++) for (let b = 0; b < d; b++) C[a * d + b] = sign * dKe[loc[a] * m + loc[b]];
  let maxd = 0; for (let i = 0; i < d; i++) maxd = Math.max(maxd, C[i * d + i]); if (maxd <= 0) return true;
  const pidx = new Int32Array(d); for (let a = 0; a < d; a++) pidx[a] = perm[freeOf[dofs[loc[a]]]];
  const vecs = [];
  for (;;) {
    let p = -1, best = 1e-12 * maxd; for (let i = 0; i < d; i++) { const v = C[i * d + i]; if (v > best) { best = v; p = i; } }
    if (p < 0) break;
    const pv = Math.sqrt(best), w = new Float64Array(d); for (let i = 0; i < d; i++) w[i] = C[i * d + p] / pv;
    for (let i = 0; i < d; i++) { const wi = w[i]; if (wi === 0) continue; const row = i * d; for (let j = 0; j < d; j++) C[row + j] -= wi * w[j]; }
    for (let i = 0; i < d; i++) C[i * d + p] = C[p * d + i] = 0;
    vecs.push(w);
  }
  this.lastRank = vecs.length;
  return this._applyRankOnes(pidx, vecs, sign);
};

/** Apply sign * sum w_t w_t^T (w_t supported on the permuted indices pidx) to the multifrontal factor. Returns false if a downdate lost positive definiteness. */
FemSolver.prototype._applyRankOnes = function (pidx, vecs, sign) {
  const n = this.nFree, { tPivStart, tP, tBorder, tParent, L, frontOfCol } = this;
  const B = 16; if (!this._W) this._W = [];
  for (let b = 0; b < B; b++) if (!this._W[b]) this._W[b] = new Float64Array(n);
  const rowIdx = this._rowIdx && this._rowIdx.length >= this.maxM ? this._rowIdx : (this._rowIdx = new Int32Array(this.maxM));
  const mark = this._mark || (this._mark = new Int32Array(this.nt)); let stamp = (this._stamp = (this._stamp || 0) + 1);
  const front = new Set(); for (let a = 0; a < pidx.length; a++) { let t = frontOfCol[pidx[a]]; while (t >= 0 && !front.has(t)) { front.add(t); t = tParent[t]; } }
  const path = Int32Array.from(front).sort();
  let ok = true;
  for (let b0 = 0; b0 < vecs.length && ok; b0 += B) {
    const nb = Math.min(B, vecs.length - b0);
    for (let v = 0; v < nb; v++) { const W = this._W[v], w = vecs[b0 + v]; for (let a = 0; a < pidx.length; a++) W[pidx[a]] = w[a]; }
    for (let k = 0; k < path.length && ok; k++) {
      const t = path[k], p = tP[t], ps = tPivStart[t], border = tBorder[t], bd = border.length, m = p + bd, Lt = L[t];
      for (let i = 0; i < m; i++) rowIdx[i] = i < p ? ps + i : border[i - p];
      for (let c = 0; c < p; c++) {
        const j = ps + c;
        for (let v = 0; v < nb; v++) {
          const W = this._W[v], wj = W[j]; if (wj === 0) continue;
          const dc = c * p + c, lj = Lt[dc], r2 = lj * lj + sign * wj * wj;
          if (!(r2 > 1e-14 * lj * lj)) { ok = false; break; }
          const r = Math.sqrt(r2), cc = r / lj, sn = wj / lj; Lt[dc] = r;
          if (sign > 0) for (let i = c + 1; i < m; i++) { const q = i * p + c, g = rowIdx[i], wi = W[g], l2 = (Lt[q] + sn * wi) / cc; W[g] = cc * wi - sn * l2; Lt[q] = l2; }
          else for (let i = c + 1; i < m; i++) { const q = i * p + c, g = rowIdx[i], wi = W[g], l2 = (Lt[q] - sn * wi) / cc; W[g] = cc * wi - sn * l2; Lt[q] = l2; }
        }
        if (!ok) break;
      }
    }
    for (let v = 0; v < nb; v++) { const W = this._W[v]; for (const t of path) { const p = tP[t], ps = tPivStart[t], border = tBorder[t]; for (let i = 0; i < p; i++) W[ps + i] = 0; for (let i = 0; i < border.length; i++) W[border[i]] = 0; } }
  }
  return ok;
};

/**
 * A held model: the factor of the current stiffness plus the load vector. edit() changes element stiffnesses and returns the new displacements;
 * solveCase() solves another load case on the held factor. When a downdate fails, or when the residual passes `tolerance`, the matrix is refactored from
 * scratch and the result says so.
 */
export class FemSession {
  /** @param {FemSolver} solver  assembled and factored  @param {Float64Array} f  load vector of the current case */
  constructor(solver, f, { tolerance = 1e-9 } = {}) { this.s = solver; this.f = f; this.tolerance = tolerance; this.x = solver.solve(f); this.edits = 0; this.refactors = 0; }

  /**
   * @param {{dofs: ArrayLike<number>, dKe: Float64Array}[]} changes  dKe = new element matrix minus old one (symmetric, semidefinite)
   * @returns {{x: Float64Array, ms: number, residual: number, residualMs: number, refactored: boolean, reason: string, rank: number}}
   */
  edit(changes) {
    const s = this.s, t0 = now(); let ok = true, rank = 0;
    for (const c of changes) { if (ok) { ok = s.updateRank(c.dofs, c.dKe); rank += s.lastRank || 0; } else s.addElement(c.dofs, c.dKe); }
    let refactored = false, reason = '';
    if (!ok) { s.factor(); refactored = true; reason = 'a downdate lost positive definiteness'; this.refactors++; }
    let x = s.solve(this.f); const ms = now() - t0;
    const t1 = now(); let residual = s.residual(x, this.f); const residualMs = now() - t1;
    if (residual > this.tolerance) { const t2 = now(); s.factor(); x = s.solve(this.f); refactored = true; reason = 'residual ' + residual.toExponential(1) + ' passed the tolerance'; residual = s.residual(x, this.f); this.refactors++; return this._done(x, ms + now() - t2, residual, residualMs, refactored, reason, rank); }
    return this._done(x, ms, residual, residualMs, refactored, reason, rank);
  }
  _done(x, ms, residual, residualMs, refactored, reason, rank) { this.x = x; this.edits++; return { x, ms, residual, residualMs, refactored, reason, rank }; }

  /** Another load case on the held factor: one forward and one backward pass. Optionally make it the current case. */
  solveCase(f2, makeCurrent = false) { const x = this.s.solve(f2); if (makeCurrent) { this.f = f2; this.x = x; } return x; }
}
// ==================================================================================================== dense kernel
/**
 * Partial Cholesky of an m x m symmetric matrix held as the lower triangle of a row-major array F: the first p columns are factored,
 * F[p.., p..] becomes the Schur complement. Blocked by 64 columns. The panel below each diagonal block is copied to a packed array so that
 * the rank-64 update of the trailing matrix reads contiguous memory, and the update works on 2 x 2 blocks of the trailing matrix.
 * Returns -1, or the index of a pivot that was not positive.
 */
export function partialCholesky(F, m, p, panel, invd) {
  const NB = 64;
  for (let k0 = 0; k0 < p; k0 += NB) {
    const k1 = Math.min(k0 + NB, p), kb = k1 - k0;
    // diagonal block, unblocked
    for (let j = k0; j < k1; j++) {
      const rj = j * m; let d = F[rj + j];
      for (let q = k0; q < j; q++) d -= F[rj + q] * F[rj + q];
      if (!(d > 1e-300)) return j;
      d = Math.sqrt(d); F[rj + j] = d; const id = 1 / d; invd[j - k0] = id;
      for (let i = j + 1; i < k1; i++) {
        const ri = i * m; let s = F[ri + j];
        for (let q = k0; q < j; q++) s -= F[ri + q] * F[rj + q];
        F[ri + j] = s * id;
      }
    }
    if (k1 >= m) continue;
    // panel below the block: L21 = A21 L11^-T, then pack it
    for (let i = k1; i < m; i++) {
      const ri = i * m, pb = (i - k1) * kb;
      for (let kk = k0; kk < k1; kk++) {
        const rk = kk * m; let s = F[ri + kk];
        for (let q = k0; q < kk; q++) s -= F[ri + q] * F[rk + q];
        s *= invd[kk - k0]; F[ri + kk] = s; panel[pb + kk - k0] = s;
      }
    }
    // trailing update  F[i][j] -= sum_q P[i][q] P[j][q]  (lower triangle only), 2 x 2 blocks
    const rows = m - k1; let i = k1;
    for (; i + 1 < m; i += 2) {
      const a0 = (i - k1) * kb, a1 = a0 + kb, r0 = i * m, r1 = r0 + m; let j = k1;
      for (; j + 1 <= i; j += 2) {
        const b0 = (j - k1) * kb, b1 = b0 + kb; let s00 = 0, s01 = 0, s10 = 0, s11 = 0;
        for (let q = 0; q < kb; q++) { const x0 = panel[a0 + q], x1 = panel[a1 + q], y0 = panel[b0 + q], y1 = panel[b1 + q]; s00 += x0 * y0; s01 += x0 * y1; s10 += x1 * y0; s11 += x1 * y1; }
        F[r0 + j] -= s00; F[r0 + j + 1] -= s01; F[r1 + j] -= s10; F[r1 + j + 1] -= s11;
      }
      for (; j <= i + 1; j++) {                       // the diagonal corner of the two rows
        const b0 = (j - k1) * kb; let s0 = 0, s1 = 0;
        for (let q = 0; q < kb; q++) { const y0 = panel[b0 + q]; s0 += panel[a0 + q] * y0; s1 += panel[a1 + q] * y0; }
        if (j <= i) F[r0 + j] -= s0;
        F[r1 + j] -= s1;
      }
    }
    if (i < m) {                                      // odd last row
      const a0 = (i - k1) * kb, r0 = i * m;
      for (let j = k1; j <= i; j++) { const b0 = (j - k1) * kb; let s0 = 0; for (let q = 0; q < kb; q++) s0 += panel[a0 + q] * panel[b0 + q]; F[r0 + j] -= s0; }
    }
  }
  return -1;
}

// ==================================================================================================== nested dissection
/**
 * Nested dissection of the node graph. Returns { pivots: Int32Array[], children: number[][] } with tree nodes in postorder (children first).
 * With coordinates the cut is geometric; without, it is the middle level of a breadth-first search.
 */
function nestedDissection(nn, nfStart, adjStart, adj, coords, leaf) {
  const seg = []; for (let a = 0; a < nn; a++) if (nfStart[a + 1] > nfStart[a]) seg.push(a);
  const arr = Int32Array.from(seg); const pivots = [], children = [];
  const side = new Uint8Array(nn), lvl = new Int32Array(nn).fill(-1);
  const newTree = (piv, kids) => { pivots.push(piv); children.push(kids); return pivots.length - 1; };

  function rec(lo, hi) {
    const len = hi - lo, roots = [];
    if (len <= 0) return roots;
    if (len <= leaf) { roots.push(newTree(arr.slice(lo, hi), [])); return roots; }
    let left, right, sep;
    if (coords) ({ left, right, sep } = geometricSplit(lo, hi)); else ({ left, right, sep } = levelSplit(lo, hi));
    let p = lo; for (const v of left) arr[p++] = v; const rlo = p; for (const v of right) arr[p++] = v; const slo = p; for (const v of sep) arr[p++] = v;
    if (left.length === 0 && right.length === 0) { roots.push(newTree(arr.slice(lo, hi), [])); return roots; }   // clique: nothing to split
    const kids = [...rec(lo, rlo), ...rec(rlo, slo)];
    if (sep.length === 0) return kids;
    roots.push(newTree(Int32Array.from(sep), kids)); return roots;
  }

  function geometricSplit(lo, hi) {
    const len = hi - lo; const mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity];
    for (let i = lo; i < hi; i++) for (let a = 0; a < 3; a++) { const v = coords[arr[i] * 3 + a]; if (v < mn[a]) mn[a] = v; if (v > mx[a]) mx[a] = v; }
    let axis = 0, ext = mx[0] - mn[0]; for (let a = 1; a < 3; a++) if (mx[a] - mn[a] > ext * (1 + 1e-12)) { axis = a; ext = mx[a] - mn[a]; }
    const keys = new Float64Array(len), order = new Uint32Array(len);
    for (let i = 0; i < len; i++) { keys[i] = coords[arr[lo + i] * 3 + axis]; order[i] = i; }
    order.sort((x, y) => keys[x] - keys[y] || x - y);
    const items = new Int32Array(len), sk = new Float64Array(len);
    for (let i = 0; i < len; i++) { items[i] = arr[lo + order[i]]; sk[i] = keys[order[i]]; }
    const mid = len >> 1, t = sk[mid]; let a1 = mid; while (a1 > 0 && sk[a1 - 1] === t) a1--; let a2 = mid; while (a2 < len && sk[a2] === t) a2++;
    let cnt; if (a1 > 0 && a2 < len) cnt = (mid - a1 <= a2 - mid) ? a1 : a2; else if (a1 > 0) cnt = a1; else if (a2 < len) cnt = a2; else cnt = mid;
    for (let i = 0; i < len; i++) side[items[i]] = i < cnt ? 1 : 2;
    const sepL = [], sepR = [];
    for (let i = 0; i < len; i++) {
      const v = items[i], sv = side[v], other = 3 - sv;
      for (let q = adjStart[v]; q < adjStart[v + 1]; q++) if (side[adj[q]] === other) { (sv === 1 ? sepL : sepR).push(v); break; }
    }
    const sep = sepL.length <= sepR.length ? sepL : sepR, isSep = new Set(sep), left = [], right = [];
    for (let i = 0; i < len; i++) { const v = items[i]; if (!isSep.has(v)) (side[v] === 1 ? left : right).push(v); }
    for (let i = 0; i < len; i++) side[items[i]] = 0;
    return { left, right, sep };
  }

  function levelSplit(lo, hi) {
    const len = hi - lo; for (let i = lo; i < hi; i++) side[arr[i]] = 3;          // 3 = in the current set
    const bfs = (start) => {                                                      // returns the visited nodes by level
      const order = [start]; lvl[start] = 0; side[start] = 4;
      for (let h = 0; h < order.length; h++) { const v = order[h]; for (let q = adjStart[v]; q < adjStart[v + 1]; q++) { const u = adj[q]; if (side[u] === 3) { side[u] = 4; lvl[u] = lvl[v] + 1; order.push(u); } } }
      return order;
    };
    const restore = (order) => { for (const v of order) side[v] = 3; };
    let start = arr[lo], order = bfs(start);
    if (order.length < len) {                                                     // disconnected: split off the component
      const inComp = new Set(order); const left = order.slice(), right = []; for (let i = lo; i < hi; i++) if (!inComp.has(arr[i])) right.push(arr[i]);
      for (let i = lo; i < hi; i++) { side[arr[i]] = 0; lvl[arr[i]] = -1; } return { left, right, sep: [] };
    }
    start = order[order.length - 1]; restore(order); order = bfs(start);          // second sweep from a far node: a pseudo-peripheral start
    const maxL = lvl[order[order.length - 1]];
    const count = new Int32Array(maxL + 1); for (const v of order) count[lvl[v]]++;
    let cum = 0, cut = 0; for (let l = 0; l <= maxL; l++) { if (cum + count[l] >= len / 2) { cut = l; break; } cum += count[l]; }
    if (cut === 0) cut = Math.min(1, maxL); if (cut === maxL && maxL > 0) cut = maxL - 1;
    const left = [], right = [], sep = [];
    if (maxL < 2) { for (let i = lo; i < hi; i++) { side[arr[i]] = 0; lvl[arr[i]] = -1; } return { left: [], right: [], sep: [] }; }
    for (const v of order) { const l = lvl[v]; (l < cut ? left : l > cut ? right : sep).push(v); }
    for (let i = lo; i < hi; i++) { side[arr[i]] = 0; lvl[arr[i]] = -1; }
    return { left, right, sep };
  }

  rec(0, arr.length);
  return { pivots, children };
}
