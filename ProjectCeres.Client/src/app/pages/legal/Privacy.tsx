import { useTranslation } from 'react-i18next';
import { useDocumentTitle } from '../../lib/use-document-title';
import PrivacyContentEn from './content/privacy.en';
import PrivacyContentEs from './content/privacy.es';

export function Privacy() {
  const { i18n } = useTranslation();
  useDocumentTitle('Privacy Policy');
  return (i18n.language ?? 'en').startsWith('es') ? <PrivacyContentEs /> : <PrivacyContentEn />;
}
