import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { useAuth } from '@/app/auth/auth-context';

export function AvatarMenu() {
  const [logoutOpen, setLogoutOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const navigate = useNavigate();
  const { logout } = useAuth();
  const { t } = useTranslation();

  async function handleLogout() {
    setSubmitting(true);
    try {
      await logout();
      setLogoutOpen(false);
      navigate('/login');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger
          render={
            <Button variant="ghost" size="icon" aria-label="Open user menu">
              <Avatar className="h-8 w-8">
                <AvatarFallback>U</AvatarFallback>
              </Avatar>
            </Button>
          }
        />
        <DropdownMenuContent align="end" className="w-48">
          <DropdownMenuItem onClick={() => navigate('/profile')}>
            Profile
          </DropdownMenuItem>
          <DropdownMenuItem onClick={() => navigate('/security')}>
            Security
          </DropdownMenuItem>
          <DropdownMenuSeparator />
          <DropdownMenuItem onClick={() => setLogoutOpen(true)}>
            Logout
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <Dialog open={logoutOpen} onOpenChange={setLogoutOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t('auth.logout.title')}</DialogTitle>
          </DialogHeader>
          <p className="text-muted-foreground">{t('auth.logout.body')}</p>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setLogoutOpen(false)}
              disabled={submitting}
            >
              {t('auth.logout.cancel')}
            </Button>
            <Button
              variant="destructive"
              onClick={() => void handleLogout()}
              disabled={submitting}
            >
              {submitting ? t('auth.logout.submitting') : t('auth.logout.submit')}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
