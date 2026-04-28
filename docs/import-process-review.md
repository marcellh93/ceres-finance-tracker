# Import System Strategy & Implementation Plan (Project Ceres)

## Purpose

This document defines a practical, user-friendly strategy for handling financial data imports in Project Ceres, particularly addressing real-world limitations such as banks (e.g., BBVA Spain) providing **Excel or PDF instead of CSV**.

The goal is to ensure:

> “Users can upload whatever their bank provides, and the system handles it with minimal friction.”

---

# Problem Summary

Many European banks:

- Provide **Excel (.xlsx)** exports
- Provide **PDF statements**
- Do **not provide CSV reliably**

This creates a UX issue:

- Users must manually convert files
- Import becomes frustrating and error-prone
- Feature adoption drops significantly

---

# Design Goal

Shift from:

> “We support CSV import”

To:

> “We support bank file import”

---

# Core Concept: Import Pipeline

All input formats must pass through a unified pipeline:

```text
Input File → Parsing → Normalization → Mapping → Validation → Import
```

---

## Internal Data Model

All formats should be converted into a unified structure:

```csharp
ImportRow
{
    DateTime Date;
    decimal Amount;
    string Description;
    string? Account;
}
```

This allows:

- CSV, Excel, and future formats to reuse the same logic
- Clean separation between parsing and business rules

---

# Supported Formats Strategy

## 1. Excel (.xlsx) — HIGH PRIORITY

### Why

- Structured and reliable
- Common format in EU banks
- Easy to parse programmatically

---

### Implementation

Recommended libraries:

- ClosedXML (preferred for simplicity)
- EPPlus (alternative)

---

### Flow

```text
.xlsx file → Parse worksheet → Extract rows → Convert to ImportRow → Continue pipeline
```

---

### Requirements

- Detect active worksheet
- Handle header row
- Ignore empty rows
- Normalize numeric formats

---

## 2. CSV — EXISTING SUPPORT

CSV remains supported and feeds directly into the same pipeline.

No architectural changes required.

---

## 3. PDF — DEFERRED / LIMITED SUPPORT

### Problem

PDF files:

- Are not structured
- Depend on visual layout
- Are unreliable to parse consistently

---

### Recommendation

Do NOT implement full PDF parsing initially.

---

### Optional Approach (Phase 3+)

- Integrate external tools (e.g., Tabula, Apache Tika)
- Provide semi-automated extraction
- Require user confirmation

---

### Interim UX Option

- Allow PDF upload
- Show preview
- Ask user to confirm or manually input extracted data

---

# Import Profiles (Critical Feature)

## Existing Concept

`CsvImportProfile` already exists in Phase 2.

---

## Extension

Profiles must support:

- CSV and Excel
- Column mappings
- Date formats
- Decimal formats
- Debit/Credit mapping rules

---

## Goal

> First import = configuration
> Subsequent imports = one-click process

---

# Column Mapping

Users must be able to map:

- Date column
- Amount column OR Debit/Credit columns
- Description column

---

## Special Handling

### Debit/Credit columns

If both exist:

```text
Amount = Credit - Debit
```

---

### Decimal normalization

Handle:

- European format: `1.234,56`
- Standard format: `1,234.56`

---

### Optional fields

- Running balance (ignored or stored optionally)
- Category (if present)

---

# Auto-Detection (Phase 2 Enhancement)

Reduce user effort by inferring columns.

---

## Heuristics

- Date column → highest percentage of parseable dates
- Amount column → numeric with positive/negative values
- Description → longest text column

---

## Outcome

- Pre-fill mapping UI
- User confirms instead of configuring from scratch

---

# Import UX Flow

## Step 1 — Upload

User uploads:

- Excel or CSV file

---

## Step 2 — Preview

System displays:

- First N rows
- Parsed values
- Highlighted issues

---

## Step 3 — Mapping

User:

- Confirms or adjusts column mappings
- Saves profile (optional)

---

## Step 4 — Validation

System checks:

- Missing required fields
- Invalid dates
- Invalid amounts
- Duplicate detection (via fingerprinting)

---

## Step 5 — Import

- Valid rows imported
- Invalid rows rejected with explanation

---

# Duplicate Detection

Leverage existing fingerprint logic:

- Date (± tolerance)
- Amount
- Description
- Account

---

## Behavior

- Potential duplicates marked as “Pending”
- User confirms before marking as cleared

---

# Architecture

## Services

Introduce modular import system:

```csharp
IImportParser
- Parse(file) → IEnumerable<ImportRow>

IImportService
- Process(rows)
- Validate(rows)
- Persist(rows)
```

---

## Implementations

- CsvImportParser
- ExcelImportParser

---

## Separation of Concerns

```text
Parsing → Format-specific
Normalization → Shared
Business rules → Services layer
```

---

# Implementation Roadmap

## Phase 1 (Core)

- Add Excel parsing (ClosedXML)
- Convert to ImportRow
- Integrate with existing CSV pipeline

---

## Phase 2 (UX)

- Import preview screen
- Column mapping UI
- Import profile persistence

---

## Phase 3 (Enhancements)

- Auto-detection of columns
- Improved validation messages
- Advanced duplicate detection

---

## Phase 4 (Optional)

- PDF support via external tools
- Bank integrations (Open Banking APIs)

---

# Key Principles

1. Do not force users to convert files manually
2. Accept real-world messy inputs
3. Normalize data early in the pipeline
4. Keep parsing separate from business logic
5. Optimize for repeatability (profiles)
6. Prioritize UX over strict format validation

---

# Final Positioning

The import system should feel like:

> “Upload → Confirm → Done”

Not:

> “Convert → Fix → Retry → Fail → Retry”

---

## Guiding Question

At every step:

> “Does this reduce friction for the user importing their bank data?”

If not, reconsider the approach.
