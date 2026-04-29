import { Search } from 'lucide-react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';

type SearchModalProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

export function SearchModal({ open, onOpenChange }: SearchModalProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-xl">
        <DialogHeader>
          <DialogTitle className="sr-only">Global search</DialogTitle>
          <DialogDescription className="sr-only">
            Search across transactions, accounts, categories, and reports.
          </DialogDescription>
        </DialogHeader>
        <div className="flex items-center gap-3">
          <Search className="h-5 w-5 text-muted-foreground" aria-hidden="true" />
          <Input
            autoFocus
            placeholder="Search transactions, accounts…"
            className="border-0 bg-transparent px-0 focus-visible:ring-0"
          />
        </div>
        <p className="mt-6 text-sm text-muted-foreground">
          Search is not yet wired up. The <code>/api/search</code> endpoint will be added in a follow-up plan.
        </p>
      </DialogContent>
    </Dialog>
  );
}
