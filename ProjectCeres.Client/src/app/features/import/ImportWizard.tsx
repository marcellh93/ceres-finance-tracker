import { useEffect, useRef, useState } from 'react';
import { toast } from 'sonner';
import { useDocumentTitle } from '../../lib/use-document-title';
import { StepFile } from './StepFile';
import { StepMapping } from './StepMapping';
import { StepReview } from './StepReview';
import { StepResult } from './StepResult';
import { WizardStepper, type WizardStep } from './WizardStepper';
import {
  IMPORT_URL,
  buildImportFormData,
  inferFormatFromFilename,
  type HeaderDetectionResult,
  type ImportColumnMappings,
  type ImportResult,
} from './import-api';

const EMPTY_MAPPINGS: ImportColumnMappings = {
  dateColumn:        '',
  amountColumn:      '',
  descriptionColumn: '',
  categoryColumn:    null,
  flipDebitSign:     true,
  sheetName:         null,
};

const EMPTY_HEADERS: HeaderDetectionResult = {
  headers: [],
  dateColumn: null,
  amountColumn: null,
  descriptionColumn: null,
  categoryColumn: null,
};

function mappingsFromHeaders(h: HeaderDetectionResult): ImportColumnMappings {
  return {
    dateColumn:        h.dateColumn        ?? '',
    amountColumn:      h.amountColumn      ?? '',
    descriptionColumn: h.descriptionColumn ?? '',
    categoryColumn:    h.categoryColumn,
    flipDebitSign:     true,
    sheetName:         null,
  };
}

const EMPTY_STATE = {
  step:               1 as WizardStep,
  file:               null as File | null,
  accountId:          null as string | null,
  headers:            EMPTY_HEADERS,
  selectedProfileId:  null as string | null,
  mappings:           EMPTY_MAPPINGS,
  result:             null as ImportResult | null,
};

export function ImportWizard() {
  useDocumentTitle('Import');
  const [step, setStep] = useState<WizardStep>(EMPTY_STATE.step);
  const [file, setFile] = useState<File | null>(EMPTY_STATE.file);
  const [accountId, setAccountId] = useState<string | null>(EMPTY_STATE.accountId);
  const [headers, setHeaders] = useState<HeaderDetectionResult>(EMPTY_STATE.headers);
  const [selectedProfileId, setSelectedProfileId] = useState<string | null>(EMPTY_STATE.selectedProfileId);
  const [mappings, setMappings] = useState<ImportColumnMappings>(EMPTY_STATE.mappings);
  const [result, setResult] = useState<ImportResult | null>(EMPTY_STATE.result);
  const [submitting, setSubmitting] = useState(false);

  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, [step]);

  function reset() {
    setStep(EMPTY_STATE.step);
    setFile(EMPTY_STATE.file);
    setAccountId(EMPTY_STATE.accountId);
    setHeaders(EMPTY_STATE.headers);
    setSelectedProfileId(EMPTY_STATE.selectedProfileId);
    setMappings(EMPTY_STATE.mappings);
    setResult(EMPTY_STATE.result);
  }

  function handleStepFileContinue(h: HeaderDetectionResult) {
    setHeaders(h);
    setMappings(mappingsFromHeaders(h));
    setSelectedProfileId(null);
    setStep(2);
  }

  function handleSelectProfile(id: string | null, profileMappings: ImportColumnMappings | null) {
    setSelectedProfileId(id);
    if (profileMappings) {
      setMappings({
        ...profileMappings,
        flipDebitSign: true,
      });
    } else {
      setMappings(mappingsFromHeaders(headers));
    }
  }

  function handleStepMappingContinue(next: ImportColumnMappings) {
    setMappings(next);
    setStep(3);
  }

  async function handleSubmitImport() {
    if (!file || !accountId || submitting) return;
    setSubmitting(true);
    try {
      const fd = buildImportFormData({ file, accountId, mappings });
      const response = await fetch(IMPORT_URL, { method: 'POST', body: fd });
      if (!response.ok) {
        const body = await response.json().catch(() => null) as { message?: string } | null;
        toast.error(body?.message ?? "Couldn't import. Try again.");
        return;
      }
      const importResult = (await response.json()) as ImportResult;
      setResult(importResult);
      setStep(4);
    } catch {
      toast.error("Couldn't import. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  function handleJumpBack(target: WizardStep) {
    if (target < step) setStep(target);
  }

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <header>
        <h1
          ref={headingRef}
          tabIndex={-1}
          className="text-2xl font-semibold outline-none"
        >
          Import
        </h1>
        <p className="mt-2 text-muted-foreground">
          Bring in a bank statement. Matching rows clear existing transactions;
          the rest land as new entries.
        </p>
      </header>

      <WizardStepper currentStep={step} onJumpBack={handleJumpBack} />

      {step === 1 ? (
        <StepFile
          file={file}
          accountId={accountId}
          onFileChange={setFile}
          onAccountChange={setAccountId}
          onContinue={handleStepFileContinue}
        />
      ) : null}

      {step === 2 ? (
        <StepMapping
          headers={headers}
          selectedProfileId={selectedProfileId}
          initialMappings={mappings}
          fileName={file?.name ?? ''}
          onSelectProfile={handleSelectProfile}
          onContinue={handleStepMappingContinue}
          onBack={() => setStep(1)}
        />
      ) : null}

      {step === 3 ? (
        <StepReview
          fileName={file?.name ?? ''}
          accountId={accountId ?? ''}
          mappings={mappings}
          submitting={submitting}
          onSubmit={handleSubmitImport}
          onBack={() => setStep(2)}
        />
      ) : null}

      {step === 4 && result !== null && file !== null ? (
        <StepResult
          result={result}
          selectedProfileId={selectedProfileId}
          fileFormat={inferFormatFromFilename(file.name)}
          mappings={mappings}
          onImportAnother={reset}
        />
      ) : null}
    </div>
  );
}
