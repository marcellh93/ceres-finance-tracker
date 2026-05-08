import { PagePlaceholder } from '../components/PagePlaceholder';
import { useDocumentTitle } from '../lib/use-document-title';

export function Security() {
  useDocumentTitle('Security');
  return (
    <PagePlaceholder
      title="Security"
      description="Active sessions and IP blocking land when authentication ships."
    />
  );
}
