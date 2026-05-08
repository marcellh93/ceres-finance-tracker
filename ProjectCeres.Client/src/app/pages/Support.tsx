import { PagePlaceholder } from '../components/PagePlaceholder';
import { useDocumentTitle } from '../lib/use-document-title';

export function Support() {
  useDocumentTitle('Support');
  return (
    <PagePlaceholder
      title="Support"
      description="The Support page will land in a follow-up plan."
    />
  );
}
