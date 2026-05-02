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
  CATEGORY_ARCHIVE_URL,
  type ApiErrorEnvelope,
  type CategoryListItemDto,
} from './categories-api';

type Props = {
  category: CategoryListItemDto;
  /** Called after a successful archive so the parent layout can refetch. */
  onChanged: () => void;
};

export function CategoryRowMenu({ category, onChanged }: Props) {
  const navigate = useNavigate();
  const [confirmOpen, setConfirmOpen] = useState(false);

  async function handleArchive() {
    setConfirmOpen(false);
    try {
      const response = await fetch(CATEGORY_ARCHIVE_URL(category.id), {
        method: 'PATCH',
      });
      if (response.ok) {
        toast.success('Archived.');
        onChanged();
        return;
      }
      if (response.status === 409) {
        const body = (await response.json().catch(() => null)) as ApiErrorEnvelope | null;
        toast.error(body?.error.message ?? "Couldn't archive. Try again.");
        return;
      }
      toast.error("Couldn't archive. Try again.");
    } catch {
      toast.error("Couldn't archive. Try again.");
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
          <DropdownMenuItem
            onClick={() => navigate(`/categories/${category.id}/edit`)}
          >
            Edit
          </DropdownMenuItem>
          {category.isActive ? (
            <DropdownMenuItem onClick={() => setConfirmOpen(true)}>
              Archive…
            </DropdownMenuItem>
          ) : null}
        </DropdownMenuContent>
      </DropdownMenu>

      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Archive '{category.name}'?</AlertDialogTitle>
            <AlertDialogDescription>
              You can still see archived categories with the toggle. This won't
              affect existing transactions.
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
