import { useState } from 'react';
import { ArrowDown, ArrowUp, Check, ChevronsUpDown, Minus } from 'lucide-react';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu';
import { Input } from '@/components/ui/input';
import { InputGroup, InputGroupAddon, InputGroupInput, InputGroupText } from '@/components/ui/input-group';
import { Kbd } from '@/components/ui/kbd';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Separator } from '@/components/ui/separator';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip';
import { Skeleton } from '@/components/ui/skeleton';
import { DatePickerField } from '@/components/DatePickerField';
import { PagePlaceholder } from '@/app/components/PagePlaceholder';
import {
  amountPlaceholder,
  formatAmountForDisplay,
  parseAmountToNumber,
  sanitizeAmountInput,
  stripThousandSeparators,
} from '@/app/lib/amount-format';
import { cn } from '@/lib/utils';

export function Components() {
  return (
    <TooltipProvider>
      <div className="space-y-10">
        <header>
          <h1 className="text-2xl font-semibold">Components</h1>
          <p className="mt-2 text-muted-foreground">
            shadcn/ui primitives in every state. Add new primitives as later
            plans introduce them.
          </p>
        </header>

        <section>
          <h2 className="mb-4 text-xl font-medium">Buttons</h2>
          <div className="flex flex-wrap gap-3">
            <Button>Default</Button>
            <Button variant="secondary">Secondary</Button>
            <Button variant="outline">Outline</Button>
            <Button variant="ghost">Ghost</Button>
            <Button variant="link">Link</Button>
            <Button variant="destructive">Destructive</Button>
            <Button disabled>Disabled</Button>
          </div>
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Badges</h2>
          <div className="flex flex-wrap gap-3">
            <Badge>Default</Badge>
            <Badge variant="secondary">Secondary</Badge>
            <Badge variant="outline">Outline</Badge>
            <Badge variant="destructive">Destructive</Badge>
            <Badge variant="success">Success</Badge>
            <Badge variant="warning">Warning</Badge>
            <Badge variant="info">Info</Badge>
          </div>
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Card</h2>
          <Card className="max-w-md">
            <CardHeader>
              <CardTitle>Card title</CardTitle>
            </CardHeader>
            <CardContent>Card body content.</CardContent>
          </Card>
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Tabs</h2>
          <Tabs defaultValue="one" className="max-w-md">
            <TabsList>
              <TabsTrigger value="one">One</TabsTrigger>
              <TabsTrigger value="two">Two</TabsTrigger>
              <TabsTrigger value="three">Three</TabsTrigger>
            </TabsList>
            <TabsContent value="one">Tab one panel.</TabsContent>
            <TabsContent value="two">Tab two panel.</TabsContent>
            <TabsContent value="three">Tab three panel.</TabsContent>
          </Tabs>
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Tooltip</h2>
          <Tooltip>
            <TooltipTrigger render={<Button variant="outline">Hover me</Button>} />
            <TooltipContent>Tooltip content here</TooltipContent>
          </Tooltip>
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Input</h2>
          <Input placeholder="Type here…" className="max-w-md" />
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Avatar</h2>
          <div className="flex gap-3">
            <Avatar><AvatarFallback>U</AvatarFallback></Avatar>
            <Avatar><AvatarFallback>JD</AvatarFallback></Avatar>
          </div>
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Separator</h2>
          <div className="max-w-md space-y-3">
            <p>Above</p>
            <Separator />
            <p>Below</p>
          </div>
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Dropdown menu</h2>
          <DropdownMenu>
            <DropdownMenuTrigger render={<Button variant="outline">Open menu</Button>} />
            <DropdownMenuContent>
              <DropdownMenuItem>One</DropdownMenuItem>
              <DropdownMenuItem>Two</DropdownMenuItem>
              <DropdownMenuItem>Three</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Popover</h2>
          <Popover>
            <PopoverTrigger render={<Button variant="outline">Open popover</Button>} />
            <PopoverContent>Popover content here.</PopoverContent>
          </Popover>
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Sheet</h2>
          <Sheet>
            <SheetTrigger render={<Button variant="outline">Open sheet</Button>} />
            <SheetContent side="right">
              <SheetHeader>
                <SheetTitle>Sheet title</SheetTitle>
              </SheetHeader>
              <p className="p-4 text-sm text-muted-foreground">Sheet body.</p>
            </SheetContent>
          </Sheet>
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Kbd</h2>
          <p className="text-sm">
            Press <Kbd>⌘K</Kbd> to open search.
          </p>
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Combobox recipe</h2>
          <p className="mb-4 text-sm text-muted-foreground">
            The canonical "searchable single-select" pattern. Outline trigger
            with chevron, popover-wrapped Command palette, opacity-stable Check
            icon, right-aligned muted metadata, mandatory empty fallback.
          </p>
          <DemoCombobox />
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">DatePickerField</h2>
          <p className="mb-4 text-sm text-muted-foreground">
            ISO yyyy-MM-dd in / out; locale display via{' '}
            <code className="font-mono">useSettings()</code>. Set{' '}
            <code className="font-mono">hideClear</code> on required fields to
            suppress the in-popover Clear button.
          </p>
          <DemoDatePicker />
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Money input</h2>
          <p className="mb-4 text-sm text-muted-foreground">
            Composite recipe — <code className="font-mono">&lt;InputGroup&gt;</code> +{' '}
            <code className="font-mono">amount-format.ts</code> helpers. Type{' '}
            <code className="font-mono">text</code>, inputMode{' '}
            <code className="font-mono">decimal</code>; raw on focus, display on
            blur, JS number on the wire. Demo is locked to{' '}
            <code className="font-mono">period_decimal</code> for clarity.
          </p>
          <DemoMoneyInput />
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Delta / PriorComparison row</h2>
          <p className="mb-4 text-sm text-muted-foreground">
            Arrow + colour-coded "vs prior" caption. The{' '}
            <code className="font-mono">goodWhenUp</code> flag controls polarity
            so the same component works for Income (good when up) and Expenses
            (bad when up).
          </p>
          <DemoDelta />
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">PagePlaceholder</h2>
          <p className="mb-4 text-sm text-muted-foreground">
            Standard "this route exists, real content lands later" surface.
            Focuses its <code className="font-mono">&lt;h1&gt;</code> on mount
            so screen-reader users hear the route change. Click below to mount
            it — the focus-grab only fires on user intent.
          </p>
          <DemoPagePlaceholder />
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Skeleton</h2>
          <p className="mb-4 text-sm text-muted-foreground">
            Match the rendered content's height to prevent layout shift.
            For chart cards we use 220px (<code>h-[220px]</code>).
          </p>
          <div className="flex flex-col gap-6 max-w-md">
            <div>
              <p className="mb-2 text-xs text-muted-foreground">Chart skeleton (h-[220px])</p>
              <Skeleton className="h-[220px] w-full" />
            </div>
            <div>
              <p className="mb-2 text-xs text-muted-foreground">Table skeleton (h-[400px])</p>
              <Skeleton className="h-[400px] w-full" />
            </div>
            <div>
              <p className="mb-2 text-xs text-muted-foreground">Text rows (h-5 w-32)</p>
              <div className="space-y-2">
                <Skeleton className="h-5 w-32" />
                <Skeleton className="h-5 w-32" />
                <Skeleton className="h-5 w-32" />
              </div>
            </div>
          </div>
        </section>
      </div>
    </TooltipProvider>
  );
}

// ---------------------------------------------------------------------------
// Combobox recipe demo
// ---------------------------------------------------------------------------

const CURRENCIES = [
  { id: 'eur', name: 'Euro',          meta: 'EUR' },
  { id: 'usd', name: 'US Dollar',     meta: 'USD' },
  { id: 'gbp', name: 'Pound Sterling', meta: 'GBP' },
  { id: 'jpy', name: 'Japanese Yen',  meta: 'JPY' },
  { id: 'chf', name: 'Swiss Franc',   meta: 'CHF' },
];

function DemoCombobox() {
  const [open, setOpen] = useState(false);
  const [value, setValue] = useState<string | null>(null);
  const selected = CURRENCIES.find((c) => c.id === value) ?? null;

  return (
    <div className="max-w-sm">
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger
          render={
            <Button
              variant="outline"
              role="combobox"
              aria-expanded={open}
              className="w-full justify-between"
            >
              {selected ? (
                selected.name
              ) : (
                <span className="text-muted-foreground">Select currency</span>
              )}
              <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
            </Button>
          }
        />
        <PopoverContent className="p-0" align="start">
          <Command>
            <CommandInput placeholder="Search currencies…" />
            <CommandList>
              <CommandEmpty>No currencies found.</CommandEmpty>
              <CommandGroup>
                {CURRENCIES.map((c) => (
                  <CommandItem
                    key={c.id}
                    value={c.name}
                    onSelect={() => {
                      setValue(c.id);
                      setOpen(false);
                    }}
                  >
                    <Check
                      className={cn(
                        'mr-2 h-4 w-4',
                        value === c.id ? 'opacity-100' : 'opacity-0'
                      )}
                    />
                    <span className="flex-1">{c.name}</span>
                    <span className="text-xs text-muted-foreground">{c.meta}</span>
                  </CommandItem>
                ))}
              </CommandGroup>
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>
    </div>
  );
}

// ---------------------------------------------------------------------------
// DatePickerField demo
// ---------------------------------------------------------------------------

function DemoDatePicker() {
  const [optional, setOptional] = useState<string | null>(null);
  const [required, setRequired] = useState<string | null>(null);

  return (
    <div className="grid max-w-2xl gap-4 sm:grid-cols-2">
      <div className="space-y-1.5">
        <p className="text-xs uppercase tracking-wider text-muted-foreground font-medium">
          Optional (Clear button shown)
        </p>
        <DatePickerField value={optional} onChange={setOptional} />
        <p className="font-mono text-xs text-muted-foreground">
          value = {optional === null ? 'null' : `"${optional}"`}
        </p>
      </div>
      <div className="space-y-1.5">
        <p className="text-xs uppercase tracking-wider text-muted-foreground font-medium">
          Required (hideClear)
        </p>
        <DatePickerField value={required} onChange={setRequired} hideClear />
        <p className="font-mono text-xs text-muted-foreground">
          value = {required === null ? 'null' : `"${required}"`}
        </p>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Money input demo
// ---------------------------------------------------------------------------

function DemoMoneyInput() {
  const format = 'period_decimal' as const;
  const [display, setDisplay] = useState('');
  const [focused, setFocused] = useState(false);

  function handleChange(next: string) {
    // While focused: digits + decimal only, no thousands separators.
    setDisplay(sanitizeAmountInput(next, format));
  }

  function handleFocus() {
    setFocused(true);
    setDisplay((d) => stripThousandSeparators(d, format));
  }

  function handleBlur() {
    setFocused(false);
    setDisplay((d) => formatAmountForDisplay(d, format));
  }

  const wire = parseAmountToNumber(
    focused ? display : stripThousandSeparators(display, format),
    format
  );

  return (
    <div className="max-w-md space-y-3">
      <InputGroup>
        <InputGroupAddon align="inline-start">
          <InputGroupText className="text-base font-medium text-muted-foreground">
            $
          </InputGroupText>
        </InputGroupAddon>
        <InputGroupInput
          type="text"
          inputMode="decimal"
          autoComplete="off"
          placeholder={amountPlaceholder(format)}
          value={display}
          onChange={(e) => handleChange(e.target.value)}
          onFocus={handleFocus}
          onBlur={handleBlur}
          className="text-base font-medium"
        />
      </InputGroup>
      <div className="space-y-1 font-mono text-xs text-muted-foreground">
        <div>state ({focused ? 'raw / focused' : 'display / blurred'}): "{display}"</div>
        <div>wire: {Number.isFinite(wire) ? wire : 'NaN'}</div>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Delta / PriorComparison demo
// ---------------------------------------------------------------------------

function DemoDelta() {
  // Tuple: [current, prior]. prior=null => "no prior period".
  const scenarios: Array<{ label: string; pair: [number, number | null] }> = [
    { label: 'Income up (good)',     pair: [3200, 2800] },
    { label: 'Expenses up (bad)',    pair: [1800, 1500] },
    { label: 'Flat',                  pair: [3000, 3000] },
    { label: 'No prior period',       pair: [3000, null] },
  ];
  const [pair, setPair] = useState<[number, number | null]>(scenarios[0].pair);
  const [goodWhenUp, setGoodWhenUp] = useState(true);

  const [current, prior] = pair;
  const formatValue = (n: number) => `$${n.toLocaleString('en-US')}`;

  return (
    <div className="max-w-md space-y-4">
      <div className="flex flex-wrap gap-2">
        {scenarios.map((s) => (
          <Button
            key={s.label}
            size="sm"
            variant="outline"
            onClick={() => {
              setPair(s.pair);
              setGoodWhenUp(s.label.startsWith('Income') || s.label === 'Flat' || s.label === 'No prior period');
            }}
          >
            {s.label}
          </Button>
        ))}
      </div>

      <div className="rounded-md border border-border bg-card p-4">
        <div className="text-xs uppercase tracking-wider text-muted-foreground font-medium mb-2">
          Sample tile
        </div>
        <div className="text-2xl font-semibold">{formatValue(current)}</div>
        <PriorComparison
          current={current}
          prior={prior}
          formatValue={formatValue}
          goodWhenUp={goodWhenUp}
        />
      </div>

      <p className="text-xs text-muted-foreground">
        Polarity: <code className="font-mono">goodWhenUp = {String(goodWhenUp)}</code>
      </p>
    </div>
  );
}

function PriorComparison({
  current,
  prior,
  formatValue,
  goodWhenUp,
}: {
  current: number;
  prior: number | null;
  formatValue: (n: number) => string;
  goodWhenUp: boolean;
}) {
  if (prior === null) {
    return (
      <div className="mt-1 text-xs text-muted-foreground">
        No prior period to compare
      </div>
    );
  }
  const direction = current > prior ? 'up' : current < prior ? 'down' : 'flat';
  const isGood = direction === 'flat' ? null : (direction === 'up') === goodWhenUp;
  const colorClass =
    isGood === null
      ? 'text-muted-foreground'
      : isGood
        ? 'text-success'
        : 'text-destructive';
  const Icon = direction === 'up' ? ArrowUp : direction === 'down' ? ArrowDown : Minus;
  return (
    <div className={`mt-1 flex items-center gap-1 text-xs ${colorClass}`}>
      <Icon className="h-3 w-3" aria-hidden="true" />
      <span>vs {formatValue(prior)} last period</span>
    </div>
  );
}

// ---------------------------------------------------------------------------
// PagePlaceholder demo (gated to avoid focus-grab on page load)
// ---------------------------------------------------------------------------

function DemoPagePlaceholder() {
  const [mounted, setMounted] = useState(false);
  return (
    <div className="space-y-3">
      <Button variant="outline" size="sm" onClick={() => setMounted((m) => !m)}>
        {mounted ? 'Unmount' : 'Mount PagePlaceholder'}
      </Button>
      {mounted && (
        <div className="max-w-2xl">
          <PagePlaceholder
            title="Reports"
            description="This page will let you build reusable reports across your transactions."
          />
        </div>
      )}
    </div>
  );
}
