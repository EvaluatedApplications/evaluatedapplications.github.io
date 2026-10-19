using System.Text;

namespace Doctrine.Tests;

/// <summary>
/// The ARMY contract (CONTRACT.md section a). Every case is a statement about <see cref="IArmyService"/> / <see cref="IArmy"/> that a stub
/// AND the real Pleroma-backed section must satisfy. Add cases when you find a real invariant; never weaken one to make an implementation pass.
/// </summary>
public static class ArmyContract
{
    static IArmy Fresh(IArmyService s, int seed = 5, Side side = Side.Player) => s.Create(side, seed);

    static IArmy Clone(IArmyService s, IArmy a)
    {
        Expect.True(s.TryParse(a.ToCode(), out var c, out var err) && c is not null, $"army code did not parse back: {err}");
        return c!;
    }

    /// <summary>An army that has been through the whole verb set, so every field of the code is non-trivial.</summary>
    static async Task<IArmy> Worked(IArmyService s)
    {
        var a = Fresh(s);
        await a.ApplySpoilsAsync(new Spoils(30, 2), default);
        await a.DraftAsync(new DraftRequest(0, 1, [new DraftPick(2, 3), new DraftPick(4, 2)]), default);
        var legal = a.Read().Legal;
        foreach (var t in new[] { DoctrineVocabulary.Army(ArmyOrderKind.Age), DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 1), DoctrineVocabulary.Army(ArmyOrderKind.Flip, 3) })
            if (legal.Allows(t)) await a.ApplyArmyOrdersAsync(DoctrineSet.Of(t), default);
        await a.ApplyLossesAsync([new CohortLoss(0, 0, 2)], default);
        return a;
    }

    static IEnumerable<int> ArmyTokens() => Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Army);

    public static IReadOnlyList<ContractCase<IArmyService>> Cases { get; } =
    [
        new("create-is-deterministic", async s =>
        {
            Expect.Equal(Fresh(s, 11).Read().Digest(), Fresh(s, 11).Read().Digest(), "same (side, seed) must give an identical army");
            await Task.CompletedTask;
        }),
        new("fresh-army-is-well-formed", s =>
        {
            foreach (var side in new[] { Side.Player, Side.Enemy })
            {
                var r = Fresh(s, 3, side).Read();
                Expect.Equal(side, r.Side, "side");
                Expect.Equal(0, r.Round, "a fresh army starts at round 0");
                Expect.True(r.Gold >= 0, "gold is never negative");
                Expect.True(r.Soldiers > 0, "a fresh army has soldiers");
                Expect.True(r.Kinds <= Roster.Size, "at most 8 kinds (the roster: there is no ninth kind, so no separate command-span refusal exists)");
                Expect.True(r.Cohorts.All(c => (uint)c.Key < Roster.Size && c.Count > 0 && (uint)c.Slot < DoctrineVocabulary.Groups && c.Veterancy >= 0 && c.PowerPct > 0),
                    "every cohort has a valid kind, count > 0, slot 0..2, veterancy >= 0, power > 0");
                Expect.True(r.Cohorts.Select(c => c.Key).Distinct().Count() == r.Cohorts.Count, "one cohort per kind");
                Expect.True(r.Legal.Allows(0), "End is always legal");
                Expect.True(DoctrineVocabulary.Size >= 64 || (r.Legal.Bits >> DoctrineVocabulary.Size) == 0, "legal bits stay inside the vocabulary");
            }
            return Task.CompletedTask;
        }),
        new("reads-are-pure", async s =>
        {
            var a = await Worked(s);
            var d1 = a.Read().Digest(); var sc = a.Score();
            _ = a.Read(); _ = a.Read(); _ = a.Score();
            Expect.Equal(d1, a.Read().Digest(), "Read() changed the army");
            Expect.Equal(sc, a.Score(), "Score() changed");
            Expect.True(sc >= 0, "score is never negative");
        }),
        new("code-is-short-and-url-safe", async s =>
        {
            var code = (await Worked(s)).ToCode();
            Expect.True(Encoding.UTF8.GetByteCount(code) <= 600, $"an army code is a few hundred bytes at most, got {code.Length}");
            Expect.True(code.Length > 0 && code.All(ch => char.IsAsciiLetterOrDigit(ch) || "-_.~:;,+=".Contains(ch)), "code must be URL-safe text");
        }),
        new("code-round-trips-everything", async s =>
        {
            var a = await Worked(s);
            var b = Clone(s, a);
            Expect.Equal(a.Read().Digest(), b.Read().Digest(), "parsed army reads differently (a field is lost in the code)");
            Expect.Equal(a.Score(), b.Score(), "score differs after round trip");
            Expect.Equal(a.ToCode(), b.ToCode(), "re-encoding a parsed army gives a different code");
        }),
        new("parsed-army-continues-identically", async s =>
        {
            var a = await Worked(s); var b = Clone(s, a);
            foreach (var x in new[] { a, b })
            {
                await x.DraftAsync(new DraftRequest(1, 1, [new DraftPick(5, 1), new DraftPick(1, 1)]), default);
                await x.ApplyArmyOrdersAsync(DoctrineSet.Of(DoctrineVocabulary.Army(ArmyOrderKind.Age)), default);
                await x.EndRoundAsync(default);
            }
            Expect.Equal(a.Read().Digest(), b.Read().Digest(), "an army restored from its code behaves differently afterwards (hidden state is not in the code)");
        }),
        new("parse-never-throws-on-garbage", s =>
        {
            var good = Fresh(s).ToCode();
            var inputs = new List<string> { "", " ", "x", "DA1", "{}", "\u0000\u0001", new string('9', 5000), good + good, good[..(good.Length / 2)], good.Replace('1', 'Z'), good.Replace(":", ";;") };
            for (int cut = 1; cut < good.Length; cut += 7) inputs.Add(good[..cut]);
            foreach (var bad in inputs)
            {
                bool ok = s.TryParse(bad, out var army, out var err);
                if (!ok) { Expect.True(army is null, "a failed parse must return no army"); Expect.True(!string.IsNullOrEmpty(err), "a failed parse must say why"); }
                else Expect.True(army is not null, "a successful parse must return an army");
            }
            return Task.CompletedTask;
        }),
        new("armies-do-not-share-state", async s =>
        {
            var a = Fresh(s, 9); var b = Fresh(s, 9);
            var before = b.Read().Digest();
            await a.ApplySpoilsAsync(new Spoils(50, 1), default);
            await a.DraftAsync(new DraftRequest(0, 1, [new DraftPick(1, 4)]), default);
            Expect.Equal(before, b.Read().Digest(), "drafting on one army changed another");
        }),
        new("draft-spends-exact-price", async s =>
        {
            var a = Fresh(s); await a.ApplySpoilsAsync(new Spoils(40, 0), default);
            var r0 = a.Read();
            var rep = await a.DraftAsync(new DraftRequest(0, 1, [new DraftPick(3, 4)]), default);
            var r1 = a.Read();
            Expect.Equal(1, rep.Accepted.Count, "the pick should be accepted");
            Expect.Equal(Roster.PriceOf(3) * 4, rep.GoldSpent, "gold spent in the report");
            Expect.Equal(r0.Gold - rep.GoldSpent, r1.Gold, "gold after");
            Expect.Equal((r0.Cohorts.FirstOrDefault(c => c.Key == 3)?.Count ?? 0) + 4, r1.Cohorts.First(c => c.Key == 3).Count, "soldiers bought");
        }),
        new("refused-draft-changes-nothing", async s =>
        {
            var a = await Worked(s);
            var before = a.Read().Digest();
            var rep = await a.DraftAsync(new DraftRequest(0, 1, [new DraftPick(99, 1), new DraftPick(-1, 1), new DraftPick(0, 0), new DraftPick(0, -5), new DraftPick(7, 100000)]), default);
            Expect.Equal(5, rep.Refused.Count, "every bad pick is refused (and reported, not thrown)");
            Expect.True(rep.Refused.All(x => !string.IsNullOrWhiteSpace(x.Reason)), "every refusal says why");
            Expect.Equal(0, rep.Accepted.Count, "nothing accepted");
            Expect.Equal(0, rep.GoldSpent, "a refused draft spends nothing");
            Expect.Equal(before, a.Read().Digest(), "a refused draft changed the army");
        }),
        new("legal-list-matches-behaviour", async s =>
        {
            // Every army-order token: legal means it applies, not legal means it is skipped and changes nothing.
            foreach (var t in ArmyTokens())
            {
                var a = Clone(s, await Worked(s));
                var legal = a.Read().Legal.Allows(t);
                var before = a.Read().Digest();
                var rep = await a.ApplyArmyOrdersAsync(DoctrineSet.Of(t), default);
                if (legal) Expect.Equal(1, rep.Applied, $"token {DoctrineVocabulary.Name(t)} is legal but was not applied");
                else
                {
                    Expect.Equal(0, rep.Applied, $"token {DoctrineVocabulary.Name(t)} is NOT legal but was applied");
                    Expect.Equal(1, rep.Skipped, $"token {DoctrineVocabulary.Name(t)} is not legal and must be reported skipped");
                    Expect.Equal(before, a.Read().Digest(), $"a skipped token ({DoctrineVocabulary.Name(t)}) changed the army");
                }
            }
        }),
        new("some-orders-are-legal-and-some-are-not", async s =>
        {
            var legal = (await Worked(s)).Read().Legal;
            var n = ArmyTokens().Count(legal.Allows);
            Expect.True(n > 0, "a healthy army has at least one legal army order");
            Expect.True(n < ArmyTokens().Count(), "the legal list must actually restrict (otherwise it is not a list)");
        }),
        new("orders-keep-the-army-valid", async s =>
        {
            foreach (var t in ArmyTokens())
            {
                var a = Clone(s, await Worked(s));
                if (!a.Read().Legal.Allows(t)) continue;
                await a.ApplyArmyOrdersAsync(DoctrineSet.Of(t), default);
                var r = a.Read();
                Expect.True(r.Cohorts.All(c => c.Count > 0 && (uint)c.Slot < DoctrineVocabulary.Groups && (uint)c.Key < Roster.Size), $"after {DoctrineVocabulary.Name(t)} a cohort is invalid");
                Expect.True(r.Gold >= 0 && r.Kinds <= Roster.Size, $"after {DoctrineVocabulary.Name(t)} gold or span is invalid");
            }
        }),
        new("field-orders-are-not-army-orders", async s =>
        {
            var a = await Worked(s); var before = a.Read().Digest();
            var field = Enumerable.Range(1, DoctrineVocabulary.Size - 1).First(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Field);
            var rep = await a.ApplyArmyOrdersAsync(DoctrineSet.Of(field), default);
            Expect.Equal(0, rep.Applied + rep.Skipped, "field tokens are ignored by the army entirely (they belong to the battle)");
            Expect.Equal(before, a.Read().Digest(), "a field order changed the army");
        }),
        new("losses-are-exact-and-clamped", async s =>
        {
            var a = await Worked(s);
            var c0 = a.Read().Cohorts.OrderByDescending(c => c.Count).First();
            int take = Math.Min(3, c0.Count);
            int removed = await a.ApplyLossesAsync([new CohortLoss(c0.Key, c0.Slot, take)], default);
            Expect.Equal(take, removed, "soldiers removed");
            var after = a.Read().Cohorts.FirstOrDefault(c => c.Key == c0.Key)?.Count ?? 0;
            Expect.Equal(c0.Count - take, after, "cohort count after a loss");
            var left = after;
            int over = await a.ApplyLossesAsync([new CohortLoss(c0.Key, c0.Slot, left + 1000), new CohortLoss(99, 0, 5), new CohortLoss(c0.Key, c0.Slot, -4)], default);
            Expect.Equal(left, over, "a loss beyond the cohort removes only what is there; unknown kinds and negative losses remove nothing");
            Expect.True(a.Read().Cohorts.All(c => c.Count > 0), "counts never go negative (an empty cohort leaves the read)");
        }),
        new("losses-apply-once", async s =>
        {
            var a = await Worked(s); var c0 = a.Read().Cohorts.OrderByDescending(c => c.Count).First();
            var before = a.Read().Soldiers;
            await a.ApplyLossesAsync([new CohortLoss(c0.Key, c0.Slot, 2)], default);
            Expect.Equal(before - 2, a.Read().Soldiers, "one loss record removes exactly its soldiers");
        }),
        new("losses-never-raise-score", async s =>
        {
            var a = await Worked(s); var c0 = a.Read().Cohorts.First();
            var before = a.Score();
            await a.ApplyLossesAsync([new CohortLoss(c0.Key, c0.Slot, 1)], default);
            Expect.True(a.Score() <= before, "losing soldiers must not raise the score");
        }),
        new("spoils-add-gold", async s =>
        {
            var a = Fresh(s); var g = a.Read().Gold;
            await a.ApplySpoilsAsync(new Spoils(17, 0), default);
            Expect.Equal(g + 17, a.Read().Gold, "gold after spoils");
            await a.ApplySpoilsAsync(new Spoils(-5, 0), default);
            Expect.True(a.Read().Gold >= g + 17, "negative spoils never take gold away");
        }),
        new("end-of-round-advances-the-clock", async s =>
        {
            var a = Fresh(s); var r = a.Read().Round;
            await a.EndRoundAsync(default);
            Expect.Equal(r + 1, a.Read().Round, "round number");
        }),
        new("orders-see-the-state-earlier-orders-left", async s =>
        {
            // Legality is judged per token, in slot order, against the army the earlier tokens left (not once before the doctrine).
            // (a) legal then illegal: the first merge takes kind 1 into kind 0, so the second finds nothing to merge.
            var a = Fresh(s, 5); await a.ApplySpoilsAsync(new Spoils(40, 0), default);
            await a.DraftAsync(new DraftRequest(0, 1, [new DraftPick(0, 1), new DraftPick(1, 1)]), default);
            var merge0 = DoctrineVocabulary.Army(ArmyOrderKind.Merge, 0);
            Expect.True(a.Read().Legal.Allows(merge0), "setup: merging kind 1 into kind 0 is legal while both are present");
            var rep = await a.ApplyArmyOrdersAsync(DoctrineSet.Of(merge0, merge0), default);
            Expect.Equal(1, rep.Applied, "the first merge applies");
            Expect.Equal(1, rep.Skipped, "the second merge was legal before the doctrine but not after the first token: it must be skipped");
            // (b) illegal then legal: flipping a trait bit that no cohort holds gives every cohort that trait, so its aura is legal after the flip only.
            bool found = false;
            for (int seed = 1; seed <= 40 && !found; seed++)
            {
                // A Flip is paid for (case "flip-is-paid-for..."), and relabelling a whole levy to a trait nobody holds is dear: the army is given the gold.
                var b = Fresh(s, seed); await b.ApplySpoilsAsync(new Spoils(2000, 0), default); var read = b.Read();
                int held = read.Cohorts.Aggregate(0, (m, c) => m | (int)c.Traits);
                int x = Enumerable.Range(0, DoctrineVocabulary.Traits).FirstOrDefault(t => (held >> t & 1) == 0, -1);
                if (x < 0) continue;
                var aura = DoctrineVocabulary.Army(ArmyOrderKind.Aura, x);
                Expect.True(!read.Legal.Allows(aura), $"setup: an aura on trait {x} that no cohort holds must not be legal");
                rep = await b.ApplyArmyOrdersAsync(DoctrineSet.Of(DoctrineVocabulary.Army(ArmyOrderKind.Flip, x), aura), default);
                Expect.Equal(2, rep.Applied, "flip, then the aura the flip made legal: both apply (legality is read after each token)");
                Expect.Equal(0, rep.Skipped, "nothing is skipped");
                found = true;
            }
            Expect.True(found, "setup: no army in seeds 1..40 had a trait nobody holds (the case needs one)");
        }),
        new("flip-is-paid-for-and-legal-only-when-affordable", async s =>
        {
            // A Flip relabels every soldier. Each soldier whose new trait set is worth more (Roster.TraitPrice) costs the difference
            // (Roster.RelabelCost); a soldier made worse is free. The army pays the sum, and the legal list offers the flip only while it can.
            int free = 0, dear = 0, paid = 0;
            for (int bit = 0; bit < DoctrineVocabulary.Traits; bit++)
            {
                var token = DoctrineVocabulary.Army(ArmyOrderKind.Flip, bit);
                // (a) a poor army (a fresh levy and 12 gold): legal exactly when affordable, and a refused flip changes nothing.
                var poor = Fresh(s, 5); var r = poor.Read();
                long cost = r.Cohorts.Sum(c => (long)c.Count * Roster.RelabelCost(c.Traits, bit));
                Expect.Equal(cost <= r.Gold, r.Legal.Allows(token), $"flip {bit} costs {cost} against {r.Gold} gold: legal exactly when affordable");
                var before = r.Digest();
                var rep = await poor.ApplyArmyOrdersAsync(DoctrineSet.Of(token), default);
                if (cost <= r.Gold)
                {
                    Expect.Equal(1, rep.Applied, $"an affordable flip {bit} applies");
                    Expect.Equal(r.Gold - cost, poor.Read().Gold, $"flip {bit} takes exactly its cost ({cost}) in gold");
                }
                else
                {
                    Expect.Equal(1, rep.Skipped, $"an unaffordable flip {bit} is skipped");
                    Expect.Equal(before, poor.Read().Digest(), $"a refused flip {bit} changed the army");
                    dear++;
                }
                // (b) a rich army pays the same price whatever it holds, and the traits flip.
                var rich = Fresh(s, 5); await rich.ApplySpoilsAsync(new Spoils(5000, 0), default); var rr = rich.Read();
                long richCost = rr.Cohorts.Sum(c => (long)c.Count * Roster.RelabelCost(c.Traits, bit));
                rep = await rich.ApplyArmyOrdersAsync(DoctrineSet.Of(token), default);
                Expect.Equal(1, rep.Applied, $"a rich army flips {bit}");
                Expect.Equal(rr.Gold - richCost, rich.Read().Gold, $"flip {bit} of a rich army takes exactly its cost");
                if (richCost == 0) free++; else paid++;
                Expect.True(rr.Cohorts.All(c => rich.Read().Cohorts.First(x => x.Key == c.Key).Traits == (Trait)((byte)c.Traits ^ (1 << bit))), $"flip {bit} flips every cohort's trait");
            }
            Expect.True(free > 0 && paid > 0 && dear > 0, $"setup: the case needs free ({free}), paid ({paid}) and unaffordable ({dear}) flips on one army to mean anything");
        }),
        new("loss-names-its-cohort-by-kind-not-slot", async s =>
        {
            // An army has one cohort per kind, so the kind is the cohort's name; Slot on a loss is a record, never a condition.
            var a = Fresh(s, 6);
            var c0 = a.Read().Cohorts.OrderByDescending(c => c.Count).First();
            int wrong = (c0.Slot + 1) % DoctrineVocabulary.Groups;
            int removed = await a.ApplyLossesAsync([new CohortLoss(c0.Key, wrong, 1)], default);
            Expect.Equal(1, removed, "a loss applies by kind whatever slot it names");
            Expect.Equal(c0.Count - 1, a.Read().Cohorts.FirstOrDefault(c => c.Key == c0.Key)?.Count ?? 0, "the cohort lost that soldier");
        }),
        new("draft-is-decided-by-the-picks-alone", async s =>
        {
            // Round and Seed on a request are labels. Chance enters an army once, in Create; a draft never rolls.
            var a = Fresh(s, 4); await a.ApplySpoilsAsync(new Spoils(30, 0), default);
            var x = Clone(s, a); var y = Clone(s, a);
            IReadOnlyList<DraftPick> picks = [new DraftPick(2, 2), new DraftPick(5, 1), new DraftPick(99, 1)];
            var rx = await x.DraftAsync(new DraftRequest(0, 1, picks), default);
            var ry = await y.DraftAsync(new DraftRequest(7, 12345, picks), default);
            Expect.Equal(rx.GoldSpent, ry.GoldSpent, "gold spent must not depend on the request's round or seed");
            Expect.Equal(rx.Accepted.Count, ry.Accepted.Count, "accepted picks must not depend on the request's round or seed");
            Expect.Equal(rx.Refused.Count, ry.Refused.Count, "refused picks must not depend on the request's round or seed");
            Expect.Equal(x.Read().Digest(), y.Read().Digest(), "the army after a draft must not depend on the request's round or seed");
        }),
        new("veterancy-lives-on-a-ring", async s =>
        {
            // Veterancy is a position on a ring of at most RingBound places: ageing and experience move it round and it wraps to 0, so it
            // stays inside 0..RingBound-1 however long the war lasts, and power stays positive at every place.
            var a = Fresh(s, 8);
            Expect.True(a.Read().Legal.Allows(DoctrineVocabulary.Army(ArmyOrderKind.Age)), "setup: Age is legal for an army with soldiers");
            int top = 0; bool wrapped = false;
            for (int i = 0; i < 200; i++)
            {
                await a.ApplyArmyOrdersAsync(DoctrineSet.Of(DoctrineVocabulary.Army(ArmyOrderKind.Age)), default);
                if (i % 7 == 0) await a.ApplySpoilsAsync(new Spoils(0, 3), default);
                var r = a.Read();
                Expect.True(r.Cohorts.All(c => c.Veterancy >= 0 && c.Veterancy < RingBound), $"after {i + 1} ageing steps a veterancy left the ring 0..{RingBound - 1}");
                Expect.True(r.Cohorts.All(c => c.PowerPct > 0), "power stays positive at every place on the ring");
                int now = r.Cohorts.Max(c => c.Veterancy);
                if (now < top) wrapped = true;
                top = now;
            }
            Expect.True(wrapped, "veterancy never wrapped in 200 steps: it is a ring, not a counter or a ceiling");
        }),
        new("score-follows-soldiers-and-gold", async s =>
        {
            // The scale and the formula are the section's own. What must hold of ANY scale: gold counts, soldiers count, and nothing a battle
            // takes away ever raises the score.
            var a = await Worked(s);
            var s0 = a.Score();
            await a.ApplySpoilsAsync(new Spoils(5, 0), default);
            Expect.True(a.Score() > s0, "winning gold raises the score");
            var start = a.Score(); var prev = start;
            for (int guard = 0; a.Read().Cohorts.Count > 0; guard++)
            {
                Expect.True(guard < 10_000, "an army that loses a soldier at a time must run out of soldiers");
                var c = a.Read().Cohorts[0];
                await a.ApplyLossesAsync([new CohortLoss(c.Key, c.Slot, 1)], default);
                var now = a.Score();
                Expect.True(now <= prev, "losing a soldier raised the score");
                prev = now;
            }
            Expect.True(prev < start, "an army with no soldiers must be worth less than the same army with them");
        }),
    ];

    /// <summary>Veterancy places a ring may have at most (CONTRACT.md a). A section picks its own period at or below this.</summary>
    public const int RingBound = 64;
}
