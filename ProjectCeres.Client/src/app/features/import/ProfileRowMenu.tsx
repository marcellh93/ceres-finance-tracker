import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'sonner';
import { MoreHorizontal } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  IMPORT_PROFILE_BY_ID_URL,
  IMPORT_PROFILE_RECOVER_URL,
  type ImportProfileListItemDto,
} from './import-api';
import { apiFetch } from '../../lib/api-client';

type Props = {
  profile: ImportProfileListItemDto;
  onChanged: () => void;
};

export function ProfileRowMenu({ profile, onChanged }: Props) {
  const navigate = useNavigate();
  const [confirmOpen, setConfirmOpen] = useState(false);
  const archived = profile.deletedAt !== null;

  async function handleArchive() {
    setConfirmOpen(false);
    try {
      const response = await apiFetch(IMPORT_PROFILE_BY_ID_URL(profile.id), {
        method: 'DELETE',
      });
      if (response.ok) {
        toast.success('Archived.');
        onChanged();
        return;
      }
      toast.error(response.message || "Couldn't archive. Try again.");
    } catch {
      toast.error("Couldn't archive. Try again.");
    }
  }

  async function handleReactivate() {
    try {
      const response = await apiFetch(IMPORT_PROFILE_RECOVER_URL(profile.id), {
        method: 'POST',
      });
      if (response.ok) {
        toast.success('Reactivated.');
        onChanged();
        return;
      }
      toast.error("Couldn't reactivate. Try again.");
    } catch {
      toast.error("Couldn't reactivate. Try again.");
    }
  }

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger
          render={
            <Button variant="ghost" size="icon" aria-label="Row actions">
              <MoreHorizontal className="h-4 w-4" />
            </Button>
          }
        />
        <DropdownMenuContent align="end">
          {archived ? (
            <DropdownMenuItem onClick={handleReactivate}>
              Reactivate
            </DropdownMenuItem>
          ) : (
            <>
              <DropdownMenuItem
                onClick={() => navigate(`/import/profiles/${profile.id}/edit`)}
              >
                Edit
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => setConfirmOpen(true)}>
                Archive
              </DropdownMenuItem>
            </>
          )}
        </DropdownMenuContent>
      </DropdownMenu>

      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Archive '{profile.name}'?</AlertDialogTitle>
            <AlertDialogDescription>
              The profile will be hidden from the picker. You can reactivate it
              within 90 days from the Include archived view.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={handleArchive}>
              Archive
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
