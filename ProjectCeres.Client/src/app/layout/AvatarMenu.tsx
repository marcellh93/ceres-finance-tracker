import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

export function AvatarMenu() {
  const [logoutOpen, setLogoutOpen] = useState(false);
  const navigate = useNavigate();

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
            <DialogTitle>Logout</DialogTitle>
          </DialogHeader>
          <p className="text-muted-foreground">
            Logout will end your session. Wired up when authentication ships.
          </p>
        </DialogContent>
      </Dialog>
    </>
  );
}
