import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu';
import { Input } from '@/components/ui/input';
import { Kbd } from '@/components/ui/kbd';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Separator } from '@/components/ui/separator';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip';
import { Numeric } from '@/components/Numeric';
import { StatTile } from '@/components/StatTile';
import { StatRow } from '@/components/StatRow';
import { EquationRow } from '@/components/EquationRow';

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
          <h2 className="mb-4 text-xl font-medium">Stat components</h2>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-6 max-w-3xl">
            <Card>
              <CardHeader><CardTitle>StatTile (vertical)</CardTitle></CardHeader>
              <CardContent>
                <StatTile
                  label="Total Spent"
                  value={<Numeric>€ 1,234.56</Numeric>}
                  valueClassName="text-2xl font-bold text-success"
                />
              </CardContent>
            </Card>

            <Card>
              <CardHeader><CardTitle>StatRow (inline)</CardTitle></CardHeader>
              <CardContent>
                <dl className="space-y-2">
                  <StatRow label="Income" value={<Numeric className="text-success">€ 3,200.00</Numeric>} />
                  <StatRow label="Expenses" value={<Numeric className="text-destructive">€ 1,850.45</Numeric>} />
                  <StatRow label="Savings Rate" value={<Numeric>42.2%</Numeric>} />
                </dl>
              </CardContent>
            </Card>

            <Card>
              <CardHeader><CardTitle>EquationRow (compact)</CardTitle></CardHeader>
              <CardContent>
                <div className="space-y-1">
                  <EquationRow label="Liquid" value={<Numeric>€ 1,200.20</Numeric>} />
                  <EquationRow label="Bills due (7 days)" value={<Numeric>−€ 770.00</Numeric>} />
                  <EquationRow
                    label="Available today"
                    value={<Numeric className="text-base font-bold text-success">€ 430.20</Numeric>}
                  />
                </div>
              </CardContent>
            </Card>
          </div>
        </section>
      </div>
    </TooltipProvider>
  );
}
