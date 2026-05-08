import { PagePlaceholder } from '../components/PagePlaceholder';
import { useDocumentTitle } from '../lib/use-document-title';

export function NotFound() {
  useDocumentTitle('Not Found');
  return (
    <PagePlaceholder
      title="Page not found"
      description="The page you tried to open doesn't exist."
    />
  );
}
