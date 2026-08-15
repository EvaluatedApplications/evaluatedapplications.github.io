# HoloDb Analytics: the plan

## What it is

A hosted analytics database you talk to with plain SQL over HTTP. You push your rows in, you run
SUM / COUNT / GROUP BY, you get JSON back. The trick is that it keeps the answers to those
aggregates maintained as it goes, so a SUM over ten million rows is a fixed-size read, not a scan of
the whole table. That is why it is fast, and it is why it is cheap to run.

I priced it the same way the big analytics warehouses do, per terabyte a query processes, so the bill
is a line people already understand. Then I set the rate at a tenth of theirs. I can do that and still
make money on almost every query, because they pay to scan the table every single time and I do not.

## Why this wins

- **Same bill, smaller number.** BigQuery charges about $6.25 per TB scanned. I charge $0.625. Ten
  times cheaper, on the exact metric they already put on the invoice. Nobody has to learn a new
  pricing model to see the saving.
- **The saving is structural, not a loss leader.** I am not buying market share by selling below
  cost. My cost to answer an aggregate is near zero because I do not re-scan. The whole discount comes
  out of margin I can afford to give away.
- **The proof ships inside the product.** Every query response includes what BigQuery and Athena would
  have charged for the same query. The customer watches themselves save money, request by request.

## The numbers (measured, not guessed)

A realistic dashboard: 10 million rows, 100,000 query refreshes a month.

- The customer pays about **$37 a month**. On BigQuery the same dashboard is about **$370**.
- It costs me about **$0.68 a month** to serve. That is **98% gross margin**.
- Per-query compute is around half a nano-dollar. The real cost is keeping the data resident, and most
  of that sits on cheap SSD through the larger-than-memory tier, not in RAM.
- It cannot be gamed by hammering queries: every query is priced roughly a hundred thousand times
  above what it costs me to answer, so a flood of queries pays me, it does not bleed me.

All of that comes out of a model I can re-run with different assumptions (`HoloDb.Bench econ`), and the
per-query costs are measured on the real engine, not made up.

## How a customer gets started

The whole point is that it is easy and there is nothing to lose by trying.

1. Sign up with an email. No card. You get an API key and **$5 of free credits** on the spot.
2. Create a table, push your rows, run SQL. First useful query in a couple of minutes.
3. Every answer tells you what you would have paid a warehouse, so the value is obvious immediately.
4. When your credits run low, you top up. I take PayPal. Credits are prepaid, so you can never get a
   surprise bill: when the balance hits zero, queries pause until you add more.

That funnel is already built and tested end to end: signup grants the free credits, queries debit
them, running out returns a clean "top up" response, a PayPal top-up adds credits, and you are going
again. One account can never spend from another's data or another's payment.

## Pricing

- **$0.625 per TB processed**, 10 MB minimum per query (same minimum BigQuery uses). Ten times under
  BigQuery, eight times under Athena.
- **Free tier:** $5 of credits on signup, no card required. Enough to genuinely try it on real data.
- **Prepaid credits, topped up with PayPal.** Simple, no invoicing surprises, and it doubles as the
  abuse cap: a free account can only ever spend its free grant.

## Getting the word out

The positioning is one sentence: *BigQuery-style analytics, ten times cheaper, free to try.*

- **The product is the advert.** The savings line in every response is built-in word of mouth. People
  screenshot "this query would have cost $0.004 on BigQuery, it cost me $0.0004."
- **Show, don't tell.** A "Show HN", a post in r/dataengineering, dev.to, Lobsters, with the benchmark
  numbers and a live cost calculator on the landing page. Data engineers respond to real figures.
- **Comparison pages.** "HoloDb vs BigQuery", "vs Athena", "vs MotherDuck", each with the calculator
  and the honest caveats. These catch high-intent search traffic.
- **Search ads** on "bigquery alternative", "cheap analytics database", "reduce bigquery cost". Small
  budget, high intent, easy to measure against signups.
- **The free custom-build offer** (see free-build-offer.md) as a top-of-funnel magnet: I build
  someone's thing for free on this stack, it becomes a reference customer and a story.
- **Cloud marketplaces** later, as a second channel: a bring-your-own-license image for people who
  want it in their own account.

## What it costs me to run

- Autoscaling containers (one engine per account, packed onto shared nodes, idle ones scale to zero).
- Residency: a little RAM for the hot accumulators and cache, the bulk on cheap SSD.
- PayPal fees on top-ups (roughly 3.5% plus a fixed bit; it comes off the top-up, not the margin).
- The free-credit grant. At near-zero marginal cost a free signup costs me pennies unless someone
  abuses it, and prepaid credits plus email verification plus per-IP limits cap that.

Fixed cost is low because the fleet scales to zero when nobody is querying. Break-even is a handful of
active paying dashboards covering the control plane and a minimum node or two.

## Honest risks and where I am not done

- **It is still a prototype next to a mature warehouse.** Durability, high availability, backups, and a
  support commitment are not at production-database bar yet. So I start with analytics that are hot and
  valuable but not life-or-death, and I am upfront about it rather than pretending.
- **One giant single tenant needs sharding, which I have not built.** Almost every analytics customer
  fits on a node, so this is a "later" problem, but it is a real limit and I will not hide it.
- **Trust in a new vendor holding data.** I run the normal hosted-database trust model (isolation,
  hashed keys, TLS, controls), the same one Snowflake and BigQuery run. For the security-sensitive, the
  bring-your-own-license image lets them keep the data in their own account.
- **PayPal only at first** adds friction for some. Stripe is the obvious second option once there is
  volume to justify it.
- **Abuse of the free tier.** Prepaid, small grant, email verification, and rate limits handle the
  common cases; I will watch it and tighten if needed.

## Roadmap

- **Done:** the engine metering, the multi-tenant API, self-serve signup, free credits, PayPal
  top-ups, a deployable container, and a profitability model that says the pricing works.
- **Next:** the landing page with the live cost calculator; deploy onto autoscaling (Azure Container
  Apps is the natural fit); email verification and rate limits on signup; a small usage dashboard.
- **After that:** Stripe as a second payment option; the marketplace image; and single-tenant sharding
  when a customer actually needs it.

## The shape of the money

Because the cost to serve is so far below the price, nearly all revenue is margin. This is not a
business that needs huge scale to work. It needs a landing page, a cheap steady trickle of signups from
the free tier, and enough of them topping up to cover a small fixed infrastructure bill. Everything
above that is profit, and the unit economics say there is a lot of room above that line.
