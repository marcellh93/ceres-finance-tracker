import { useTranslation } from 'react-i18next';
import { useDocumentTitle } from '../../lib/use-document-title';
import LegalContentEn from './content/legal.en';
import LegalContentEs from './content/legal.es';

export function Legal() {
  const { i18n } = useTranslation();
  useDocumentTitle('Legal');
  return (i18n.language ?? 'en').startsWith('es') ? <LegalContentEs /> : <LegalContentEn />;
}
