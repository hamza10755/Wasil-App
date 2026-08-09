# Day 3 Concurrency Incident Documentation Rule

Whenever we complete a part/incident (specifically Parts 1–4, and optionally 5-6), we must document it in `docs/day-03-incidents.md` using the exact incident format:

1. **Experiment** — exactly what you fired (endpoint, how many parallel, starting state).
2. **Expected** — what a correct system should do.
3. **Observed** — what actually happened (numbers, the bad DB rows, screenshots welcome).
4. **Why** — the interleaving that caused it. Walk two requests through step by step.
5. **Fix** — what you changed and *why that closes the race*, plus what it costs.
6. **Proof** — the same experiment re-run, now behaving.
