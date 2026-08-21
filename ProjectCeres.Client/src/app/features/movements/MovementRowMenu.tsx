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
  TRANSACTION_BY_ID_URL,
  TRANSFER_BY_ID_URL,
  LIABILITY_PAYMENT_BY_ID_URL,
  type MovementType,
} from './movements-api';
import { apiFetch } from '../../lib/api-client';

type Props = {
  movementId: string;
  movementType: MovementType;
  isOpeningBalance?: boolean;
  onDeleted: () => void;
};

export function MovementRowMenu({ movementId, movementType, isOpeningBalance, onDeleted }: Props) {
  const navigate = useNavigate();
  const [confirmOpen, setConfirmOpen] = useState(false);

  const url =
    movementType === 'Transaction'
      ? TRANSACTION_BY_ID_URL(movementId)
      : movementType === 'Transfer'
        ? TRANSFER_BY_ID_URL(movementId)
        : LIABILITY_PAYMENT_BY_ID_URL(movementId);

  async function handleDelete() {
    const response = await apiFetch(url, { method: 'DELETE' });
    if (response.ok && response.status === 204) {
      toast.success('Deleted.');
      onDeleted();
      return;
    }
    // apiFetch already parses the 422 envelope; formError carries error.message.
    if (!response.ok && response.status === 422) {
      const formError = 'formError' in response ? response.formError : undefined;
      toast.error(formError ?? "Couldn't delete.");
      return;
    }
    toast.error("Couldn't delete.");
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
          <DropdownMenuItem onClick={() => navigate(`/movements/${movementId}/edit`)}>
            Edit
          </DropdownMenuItem>
          {!isOpeningBalance && (
            <DropdownMenuItem onClick={() => setConfirmOpen(true)}>
              Delete
            </DropdownMenuItem>
          )}
        </DropdownMenuContent>
      </DropdownMenu>

      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete this movement?</AlertDialogTitle>
            <AlertDialogDescription>This action cannot be undone.</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={handleDelete}>Delete</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
