import { PagePlaceholder } from '../components/PagePlaceholder';

export function NotFound() {
  return (
    <PagePlaceholder
      title="Page not found"
      description="The page you tried to open doesn't exist."
    />
  );
}
