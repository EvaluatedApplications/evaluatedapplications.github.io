# Free software, built by me, on my own stack. For the best ideas.

I build software — all of it, top to bottom. The database, the machine-learning engine, the
concurrency runtime, the 3D engine: I wrote them from scratch, and they're genuinely different
from what's out there. Holographic storage. Aggregates that answer in constant time instead of
scanning. Learning models small enough to run on a laptop with no GPU. Most of it is faster than
the industry-standard tools it stands next to, and I can show you the benchmarks.

Here's the offer. If you have a good idea, I'll build it for you. Free. Forever. It runs on my
own stack, hosted by me, maintained by me. You don't pay for the software, the hosting, or the
upkeep — and this isn't a free tier that quietly expires or throttles you into a subscription.
Free means free.

There's one catch, and it's the honest one: I choose the ideas I find most interesting. I'd
rather build one thing that excites me properly than ten that don't. So pitch me your best.

## What this covers

Because I own the whole stack, I can build across a lot of ground — and make the parts talk to
each other in ways an off-the-shelf assembly can't:

- **Websites** — marketing sites, dashboards, data-heavy pages that stay fast because the
  database underneath them doesn't scan the table to answer a question.
- **Apps** — cross-platform from one codebase: desktop, mobile, and web together.
- **Small learning models for e-commerce** — recommendations, demand forecasting, adaptive
  pricing, churn signals — running on tiny models that learn from *your* data on *your* stack,
  not shipped off to someone else's cloud to be sold back to you.
- **Data & analytics** — a real SQL database that answers SUM / COUNT / GROUP BY / MIN-MAX /
  DISTINCT in constant time, whether embedded in your app or run as a server.
- **Concurrency-heavy backends** — services that fan a job out across far more work than they
  have threads, hold every backend (DB pool, API, disk) at its real limit, and don't fall over
  under a flood. The runtime even tunes its own concurrency now, learning the right level as it
  runs.
- **Games & 3D** — a voxel engine with native level-of-detail and its own pathfinding.

If your idea sits between two of those, good — that's usually where the interesting work is.

## The stack it runs on

Every build sits on packages I wrote and maintain. They're on NuGet; they're real:

- **EvalApp** — the concurrency runtime. Governs how much runs at once, gates every resource,
  retries and compensates when things fail, and tunes the in-flight level itself. One dependency
  in place of a stack of libraries.
- **HoloDb** — a holographic SQL database. It keeps aggregates as maintained accumulators, so a
  SUM over ten million rows is a fixed-size read, not a scan. It beats SQLite, DuckDB, and SQL
  Server on most queries, embedded or over the wire.
- **AlgFormer / HoloFormer** — the machine-learning core. Holographic-native transformers that
  learn from experience, small enough to run anywhere, with no GPU and no external ML runtime.
- **Phasor** — the vector-symbolic codec the models are built on.
- **Tracer** — navigation and pathfinding.
- **HoloVoxel** — a 3D voxel engine with native level-of-detail.

## Why I'm doing this

The honest version: I've built a lot, and the best way to prove it's real is to put it to work on
someone's actual problem. You get software that would cost real money — built, hosted, and looked
after for free. I get interesting problems to solve on tech I believe in. If it grows into
something bigger, we can talk about that then. If it doesn't, you keep what I built, for good.

Pitch me. The best ideas win.
