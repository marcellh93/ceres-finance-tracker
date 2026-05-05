import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useReviewCount } from './ReviewCountProvider';
import { ReconciliationList } from './ReconciliationList';
import { TransferList } from './TransferList';

type TabKey = 'reconciliations' | 'transfers';

function pickDefaultTab(reconciliationCount: number, transferCount: number): TabKey {
  if (reconciliationCount > 0) return 'reconciliations';
  if (transferCount > 0) return 'transfers';
  return 'reconciliations';
}

function readTabParam(value: string | null): TabKey | null {
  if (value === 'reconciliations' || value === 'transfers') return value;
  return null;
}

export function ReviewLayout() {
  const [searchParams, setSearchParams] = useSearchParams();
  const { reconciliationCount, transferCount, loading, refresh } = useReviewCount();

  const initialFromUrl = readTabParam(searchParams.get('tab'));
  const [activeTab, setActiveTab] = useState<TabKey>(initialFromUrl ?? 'reconciliations');
  // Refs don't trigger re-renders; we just need to remember whether the user
  // (or the URL) has explicitly chosen a tab so that A3 doesn't override them.
  const userTouchedTab = useRef(initialFromUrl !== null);

  // A3: post-load adjustment when no explicit ?tab= and user hasn't clicked.
  useEffect(() => {
    if (initialFromUrl !== null) return;
    if (loading) return;
    if (userTouchedTab.current) return;
    const target = pickDefaultTab(reconciliationCount, transferCount);
    if (target !== activeTab) setActiveTab(target);
  }, [loading, reconciliationCount, transferCount, initialFromUrl, activeTab]);

  function handleTabChange(next: string) {
    const t = next as TabKey;
    setActiveTab(t);
    userTouchedTab.current = true;
    setSearchParams({ tab: t }, { replace: true });
  }

  return (
    <div className="mx-auto max-w-4xl py-6">
      <h1 className="text-2xl font-semibold" tabIndex={-1}>Review</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        Triage rows the importer staged for you. Confirm what's right, dispute what's wrong,
        link transfers to their other side.
      </p>

      <Tabs value={activeTab} onValueChange={(v) => handleTabChange(v as string)} className="mt-6">
        <TabsList>
          <TabsTrigger
            value="reconciliations"
            // Mark the tab as user-touched even when clicking the already-active tab
            // (base-ui's onValueChange does NOT fire when value is unchanged, so the
            // suppression flag would otherwise never be set in that case).
            onClick={() => { userTouchedTab.current = true; }}
          >
            Reconciliations
            {reconciliationCount > 0 && (
              <span
                className="ml-2 rounded-full bg-primary/10 px-2 py-0.5 text-xs"
                data-testid="reconciliations-count"
              >
                {reconciliationCount}
              </span>
            )}
          </TabsTrigger>
          <TabsTrigger
            value="transfers"
            onClick={() => { userTouchedTab.current = true; }}
          >
            Transfers
            {transferCount > 0 && (
              <span
                className="ml-2 rounded-full bg-primary/10 px-2 py-0.5 text-xs"
                data-testid="transfers-count"
              >
                {transferCount}
              </span>
            )}
          </TabsTrigger>
        </TabsList>
        <TabsContent value="reconciliations" className="mt-6">
          {activeTab === 'reconciliations' && <ReconciliationList onChanged={refresh} />}
        </TabsContent>
        <TabsContent value="transfers" className="mt-6">
          {activeTab === 'transfers' && <TransferList onChanged={refresh} />}
        </TabsContent>
      </Tabs>
    </div>
  );
}
