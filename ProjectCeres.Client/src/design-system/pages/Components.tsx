import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip';

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
            <TooltipTrigger>
              <Button variant="outline">Hover me</Button>
            </TooltipTrigger>
            <TooltipContent>Tooltip content here</TooltipContent>
          </Tooltip>
        </section>
      </div>
    </TooltipProvider>
  );
}
