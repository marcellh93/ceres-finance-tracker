import { PagePlaceholder } from '../components/PagePlaceholder';
import { useDocumentTitle } from '../lib/use-document-title';

export function Profile() {
  useDocumentTitle('Profile');
  return (
    <PagePlaceholder
      title="Profile"
      description="Your profile lands when authentication ships."
    />
  );
}
