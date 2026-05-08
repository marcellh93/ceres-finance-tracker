# Movements Mobile Card Layout — 2026-05-08

**Status:** Approved 2026-05-08. Implementation pending.

**Context:** During the Tier 3 polish browser-pass on iPhone SE (375 px), the Movements list page revealed empty horizontal space on the right and a desktop-table-on-mobile feel. Industry research (Smashing Magazine mobile-banking essentials, Pencil & Paper data-table UX, UXmatters mobile tables) converges on the **card-list pattern** for transaction views on mobile: cards beat tables for scannability, scale to viewport width, and avoid horizontal scroll.

This is **not** a Tier 3 polish item — it's a discrete mobile-responsiveness piece of work surfaced by the Tier 3 verification.

## Summary

Replace the desktop `MovementsTable` with a stacked card layout below 768 px (Tailwind's `md` breakpoint). The desktop table renders unchanged at `md` and above.

## Decisions captured during brainstorm

- **Density:** compact 3-line card, ~80 px tall.
- **Status badge & ⋮ menu:** status badge inline on line 3 (left side), ⋮ menu floating top-right corner.
- **Breakpoint:** below `md` (768 px). Tablets in landscape and desktop see the existing table.
- **Card-body tap:** navigates to `/movements/{id}/edit`. Status toggle and ⋮ menu have their own tap zones via `e.stopPropagation()`.
- **Pagination:** existing `MovementsPagination` stays unchanged below both desktop and mobile lists.

## Card structure

Compact 3-line layout:

```
┌───────────────────────────────────────────────┐
│  [Type pill]   Mar 12               −€42.50  │  Line 1
│                                                │
│  Spotify subscription                    ⋮     │  Line 2
│                                                │
│  Checking Account (BBVA)         ✓ Cleared    │  Line 3
└───────────────────────────────────────────────┘
```

- **Line 1, left:** type pill (`<Badge variant="info">` for Transaction, `bg-chart-6` for Transfer, `bg-chart-7` for LiabilityPayment — matches the existing table logic) + date in muted text using `formatDate(item.date, dateFormat)`.
- **Line 1, right:** amount via `<Numeric>`, signed, with type-driven color from the existing `amountColor` helper (lifted from `MovementsTable.tsx`).
- **Line 2, left:** description (primary text). When `description` is `null`, fall back to `categoryName` for transactions, or `MOVEMENT_TYPE_LABEL` value for transfers / liability payments, so the card always has meaningful primary copy.
- **Line 2, right:** ⋮ menu via `<MovementRowMenu>`, anchored top-right of the card.
- **Line 3, left:** account info — single `accountName` for transactions, `${sourceAccountName} → ${destAccountName}` for transfers, `${assetAccountName} → ${liabilityAccountName}` for liability payments.
- **Line 3, right:** status badge via `<MovementClearedToggle>`.

## Component contract

```tsx
// MovementsCardList.tsx
type Props = { items: MovementListItemDto[]; onRefetch: () => void };

export function MovementsCardList({ items, onRefetch }: Props) { … }
```

Same contract as `MovementsTable.tsx`. The parent (`MovementsLayout`) chooses between them by reference based on viewport.

## Markup

```tsx
<ul className="space-y-2" role="list">
  {items.map((item) => (
    <li key={`${item.movementType}-${item.id}`}>
      <article
        className={cn(
          'group relative rounded-lg border border-border bg-card text-card-foreground',
          'transition-colors [transition-duration:var(--motion-duration-base)]',
          'hover:bg-accent/40 focus-within:ring-2 focus-within:ring-ring',
        )}
      >
        <Link
          to={editHrefFor(item)}
          aria-label={`Edit ${MOVEMENT_TYPE_LABEL[item.movementType]} on ${formatDate(item.date, dateFormat)}`}
          className="block px-4 py-3 focus:outline-none"
        >
          {/* line 1, line 2 description (no menu/toggle), line 3 account (no badge) */}
        </Link>
        {/* absolute-positioned ⋮ menu (top-right) and status badge (line 3 right edge) */}
      </article>
    </li>
  ))}
</ul>
```

The `<Link>` wraps only the textual content. The ⋮ menu and status badge are absolutely-positioned siblings of the `<Link>` (similar pattern to the BudgetCombobox clear ✕ fix from earlier today): each calls `e.stopPropagation()` on its own click handler so their taps don't bubble to the card-link navigation.

`focus-within:ring-2` on the `<article>` means the whole card visibly highlights when any child (link, toggle, kebab) has focus — keyboard users get one ring per card regardless of which inner control is focused.

## Breakpoint switching

`MovementsLayout.tsx` change at the existing render branch:

```tsx
import { useMediaQuery } from '../../lib/use-media-query';
import { MovementsCardList } from './MovementsCardList';

// in component body
const isDesktop = useMediaQuery('(min-width: 768px)');

// at the existing data-render block
{data && data.items.length > 0 && (
  <div className="space-y-4">
    {isDesktop
      ? <MovementsTable items={data.items} onRefetch={refetch} />
      : <MovementsCardList items={data.items} onRefetch={refetch} />}
    <MovementsPagination totalCount={data.totalCount} pageSize={data.pageSize} />
  </div>
)}
```

`useMediaQuery` synchronously reads `matchMedia` on first render in the browser (per its source), so there is no flash on initial load. Cross-breakpoint resize triggers a re-render that swaps the rendered component.

## Edit URL

Existing routes are `/movements/:id/edit`. The Edit page (`MovementEdit.tsx`) resolves the movement type by fetching the movement by id from the API; no query string needed. The card link is therefore:

```tsx
<Link to={`/movements/${item.id}/edit`}>…</Link>
```

This matches the existing `MovementRowMenu.tsx:75` pattern.

## Action-zone tap isolation

The two interactive children inside the card need `stopPropagation` so card-body tap doesn't fire when the user means to tap the toggle or the kebab.

- **Status badge (`MovementClearedToggle`):** today its outer `<button onClick={toggle}>` has no `stopPropagation`. Wrap the consumer site in the card with a `<div onClick={(e) => e.stopPropagation()}>` (don't modify the toggle component — that keeps it untouched for desktop).
- **⋮ menu (`MovementRowMenu`):** same wrapper pattern.

Tap target sizes:

- Card body: ~80 px tall × 100 % wide → exceeds WCAG 44 × 44 minimum.
- Status badge wrapper: `min-h-11 min-w-11 flex items-center justify-end` to ensure 44 × 44 even though the visible badge is ~24 px tall.
- ⋮ menu wrapper: same `min-h-11 min-w-11` treatment.

## Accessibility

- `<ul role="list">` + `<li>` per item; screen readers announce list size.
- `<article>` per card — semantic landmark for self-contained content.
- Card link `aria-label` includes type and date so VoiceOver / TalkBack speak it as more than "Edit".
- Tab order per card: card link → status toggle → ⋮ menu.
- Focus ring: `focus-within:ring-2` on `<article>` plus `focus:outline-none` on the inner `<Link>` so the ring sits on the article boundary.
- `prefers-reduced-motion: reduce` already collapses the global `transition-colors` rule via the override in `index.css`.

## Tests

`MovementsCardList.test.tsx`:

1. Renders one `<article>` per item, preserving order.
2. Transaction card shows account name; description fallback uses category name when description is null.
3. Transfer card shows "source → destination" form; no category.
4. LiabilityPayment card shows "asset → liability" form; no category.
5. Amount sign and color match the existing `amountColor` rules (Income green, Expense red, Transfer chart-6, LiabilityPayment chart-7, opening balance neutral).
6. Card link `href` is `/movements/${id}/edit`.
7. Click on status toggle does NOT navigate to the link (stopPropagation works).
8. Click on ⋮ menu does NOT navigate.
9. Container has the implicit list role (or explicit `role="list"`); each item is reachable via `getByRole('listitem')`.
10. Card link `aria-label` describes the movement (movement type + formatted date).

## Out of scope (recorded for follow-up)

1. **Possible UX iteration:** card body could open a read-only Detail view instead of navigating to Edit directly. Pinned for re-evaluation after seeing the implementation live. The user flagged this concern during brainstorming on 2026-05-08.
2. **Same pattern for other tables (Accounts, Categories, Recurring, Reports).** Each has distinct fields and ergonomics. One feature at a time.
3. **Infinite scroll on mobile pagination.** More mobile-native UX but adds scroll-listener wiring, request deduping, end-of-list state. Worth-it later.
4. **Swipe-to-edit / swipe-to-delete gestures.** Mobile-native action affordance, not built today.

## File structure

| Path | Status |
|---|---|
| `ProjectCeres.Client/src/app/features/movements/MovementsCardList.tsx` | New |
| `ProjectCeres.Client/src/app/features/movements/MovementsCardList.test.tsx` | New |
| `ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx` | Modified — viewport switch only |

`MovementsTable.tsx`, `MovementClearedToggle.tsx`, `MovementRowMenu.tsx`, `MovementsPagination.tsx` are unchanged.

## Verification (post-implementation)

- `pnpm test --run` green at 832 + new card list tests.
- `pnpm build` clean.
- Manual: at iPhone SE (375 px), Movements page shows the card list with no horizontal scroll, full-width cards, all key fields visible per card.
- Manual: at desktop (≥ 768 px), Movements still shows the existing table.
- Manual: drag DevTools across the 768 px breakpoint while viewing the page — table ↔ cards swap cleanly without flash.
- Manual: tap card body → Edit page opens. Tap status badge → cleared/pending flips, no navigation. Tap ⋮ → menu opens, no navigation.
