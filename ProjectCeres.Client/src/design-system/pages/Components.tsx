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
import { Skeleton } from '@/components/ui/skeleton';

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
