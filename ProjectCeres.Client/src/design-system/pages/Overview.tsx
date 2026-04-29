export function Overview() {
  return (
    <div className="prose max-w-2xl">
      <h1 className="text-3xl font-semibold">Project Ceres Design System</h1>
      <p className="mt-4 text-muted-foreground">
        This is the living reference for every visual decision in the Ceres
        product. Tokens defined here drive every page; if a value is not
        listed here, it should not be hard-coded in components.
      </p>
      <p className="mt-4 text-muted-foreground">
        Use the toggle at the top of the sidebar to flip between light and
        dark modes — every swatch and component should respond.
      </p>
      <p className="mt-4 text-muted-foreground">
        Source of truth document:{' '}
        <code className="rounded bg-muted px-1 py-0.5 text-sm">
          docs/design-system.md
        </code>
      </p>
    </div>
  );
}
