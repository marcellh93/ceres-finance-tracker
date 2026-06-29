/* eslint-disable react-refresh/only-export-components -- Why: this file is a provider+hook pair; splitting into provider.tsx+context.ts would require updating 4 consumer files including test fixtures that import both from the same path. */
import { createContext, useContext, useMemo } from 'react';
import { useApi } from '../../lib/use-api';
import {
  RECONCILIATION_REVIEW_PENDING_COUNT_URL,
  TRANSFER_REVIEW_PENDING_COUNT_URL,
} from './review-api';

type Ctx = {
  reconciliationCount: number;
  transferCount: number;
  total: number;
  loading: boolean;
  refresh: () => void;
};

const ReviewCountContext = createContext<Ctx>({
  reconciliationCount: 0,
  transferCount: 0,
  total: 0,
  loading: false,
  refresh: () => {},
});

export function ReviewCountProvider({ children }: { children: React.ReactNode }) {
  const recon = useApi<number>(RECONCILIATION_REVIEW_PENDING_COUNT_URL);
  const xfer  = useApi<number>(TRANSFER_REVIEW_PENDING_COUNT_URL);

  const value = useMemo<Ctx>(() => {
    const reconciliationCount = recon.error ? 0 : recon.data ?? 0;
    const transferCount       = xfer.error  ? 0 : xfer.data  ?? 0;
    return {
      reconciliationCount,
      transferCount,
      total: reconciliationCount + transferCount,
      loading: recon.loading || xfer.loading,
      refresh: () => { recon.refetch(); xfer.refetch(); },
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps -- Why: intentionally using individual fields (recon.data, etc.) as deps to avoid invalidating the memo on every render; useApi returns a new object reference each time, so depending on the whole object would cause constant re-computation.
  }, [recon.data, recon.error, recon.loading, recon.refetch, xfer.data, xfer.error, xfer.loading, xfer.refetch]);

  return <ReviewCountContext.Provider value={value}>{children}</ReviewCountContext.Provider>;
}

export function useReviewCount(): Ctx {
  return useContext(ReviewCountContext);
}
