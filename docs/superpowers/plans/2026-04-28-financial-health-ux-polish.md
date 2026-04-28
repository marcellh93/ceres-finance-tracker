# Financial Health Card UX Polish — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Financial Health card's flat `<dl>` list with a compact horizontal 4-column bar that matches the app's white card style and shows descriptive empty-state messages.

**Architecture:** Single Razor partial change — `_HealthSnapshot.cshtml`. No service, controller, or model changes. All data is already available via `ViewBag.HealthSnapshot`. The inner 4-column grid is built with Tailwind utility classes inline (no new CSS classes).

**Tech Stack:** ASP.NET Core Razor, Tailwind CSS v3, IBM Plex Mono (already loaded via app.css)

---

### Task 1: Replace the `<dl>` layout with a 4-column bar

**Files:**
- Modify: `ProjectCeres/Views/Dashboard/_HealthSnapshot.cshtml`

This is a pure UI change — no tests to write (no new service logic, no new controller behavior). Verify visually in the browser after the change.

- [ ] **Step 1: Replace the entire file contents**

Open `ProjectCeres/Views/Dashboard/_HealthSnapshot.cshtml` and replace it with:

```razor
@using ProjectCeres.Helpers
@{
    var snapshot = ViewBag.HealthSnapshot as ProjectCeres.Services.HealthSnapshotData;
}

@if (snapshot != null)
{
    <section class="dashboard-card">
        <h2>Financial Health</h2>
        <div class="border border-gray-200 rounded-lg overflow-hidden grid grid-cols-4">

            @* — Spendable Balance — *@
            <div class="p-[14px_18px] border-r border-gray-200 @(snapshot.SpendableBalance == null ? "bg-gray-50" : "")">
                <div class="text-[10.5px] uppercase tracking-widest text-gray-400 font-medium mb-2">Spendable Balance</div>
                @if (snapshot.SpendableBalance == null)
                {
                    <div class="text-[11.5px] italic text-gray-400 leading-snug">No asset accounts found</div>
                }
                else
                {
                    <div class="text-lg font-bold @(snapshot.SpendableBalance >= 0 ? "text-green-600" : "text-red-600")"
                         style="font-family:'IBM Plex Mono',ui-monospace,monospace">
                        @snapshot.CurrencySymbol @NumberFormatHelper.FormatAmount(snapshot.SpendableBalance.Value, ViewData["NumberFormat"] as string)
                    </div>
                }
            </div>

            @* — Runway — *@
            <div class="p-[14px_18px] border-r border-gray-200 @(snapshot.RunwayMonths == null ? "bg-gray-50" : "")">
                <div class="text-[10.5px] uppercase tracking-widest text-gray-400 font-medium mb-2">Runway</div>
                @if (snapshot.RunwayMonths == null)
                {
                    <div class="text-[11.5px] italic text-gray-400 leading-snug">Needs 6 months of expense history</div>
                }
                else
                {
                    var runwayClass = snapshot.RunwayMonths > 6 ? "text-green-600"
                                    : snapshot.RunwayMonths >= 3 ? "text-amber-600"
                                    : "text-red-600";
                    <div class="text-lg font-bold @runwayClass" style="font-family:'IBM Plex Mono',ui-monospace,monospace">
                        @snapshot.RunwayMonths.Value.ToString("F1") mo
                    </div>
                }
            </div>

            @* — Income vs. Avg — *@
            <div class="p-[14px_18px] border-r border-gray-200 @(snapshot.IncomeDeltaPercent == null ? "bg-gray-50" : "")">
                <div class="text-[10.5px] uppercase tracking-widest text-gray-400 font-medium mb-2">Income vs. Avg</div>
                @if (snapshot.IncomeDeltaPercent == null)
                {
                    <div class="text-[11.5px] italic text-gray-400 leading-snug">Needs 6 months of income history</div>
                }
                else
                {
                    var incomeClass = snapshot.IncomeDeltaPercent > 0 ? "text-green-600"
                                    : snapshot.IncomeDeltaPercent < 0 ? "text-red-600"
                                    : "text-gray-400";
                    <div class="text-lg font-bold @incomeClass" style="font-family:'IBM Plex Mono',ui-monospace,monospace">
                        @(snapshot.IncomeDeltaPercent >= 0 ? "+" : "")@snapshot.IncomeDeltaPercent.Value.ToString("P1")
                    </div>
                }
            </div>

            @* — Budget Burn Rate — *@
            <div class="p-[14px_18px] @(snapshot.BudgetBurnRate == null ? "bg-gray-50" : "")">
                <div class="text-[10.5px] uppercase tracking-widest text-gray-400 font-medium mb-2">Budget Burn Rate</div>
                @if (snapshot.BudgetBurnRate == null)
                {
                    <div class="text-[11.5px] italic text-gray-400 leading-snug">No active category budgets</div>
                }
                else
                {
                    var burnClass = snapshot.BudgetBurnRate < 0.5m ? "text-green-600"
                                 : snapshot.BudgetBurnRate <= 0.8m ? "text-amber-600"
                                 : "text-red-600";
                    <div class="text-lg font-bold @burnClass" style="font-family:'IBM Plex Mono',ui-monospace,monospace">
                        @snapshot.BudgetBurnRate.Value.ToString("P1")
                    </div>
                }
            </div>

        </div>
    </section>
}
```

- [ ] **Step 2: Build and run**

```bash
dotnet run --project ProjectCeres
```

Open `https://localhost:7001` and verify:
- The Financial Health section shows a single horizontal row of 4 cells
- Spendable Balance shows `€ 4.102,85` in green with monospace font
- Runway and Income vs. Avg cells show italic gray descriptive text on a slightly off-white background
- Budget Burn Rate shows `41,0%` in green with monospace font
- The card matches the white border/rounded style of the Net Worth and Month to Date cards above it

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Views/Dashboard/_HealthSnapshot.cshtml
git commit -m "feat(dashboard): replace Financial Health dl list with 4-column bar layout"
```
