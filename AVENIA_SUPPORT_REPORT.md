# Avenia KYB Integration — Sandbox API Status Report

**Account (main) ID:** `4ffdf191-2ada-453d-9a66-35841ea1cc5d`
**Sandbox base URL:** `https://api.sandbox.avenia.io:10952`
**Auth:** RSA-SHA256 signed (`X-API-Key`, `X-API-Timestamp`, `X-API-Signature`) — verified working
**Date of testing:** 2026-04-29 → 2026-04-30

---

## Summary table

| # | Method + Endpoint | Purpose | Status |
|---|---|---|---|
| 1 | `GET /v2/account/account-info` | Auth probe / main account info | ✅ **SUCCESS** (200) |
| 2 | `GET /v2/account/sub-accounts` | List subaccounts | ✅ **SUCCESS** (200) |
| 3 | `POST /v2/account/sub-accounts` | Create COMPANY subaccount | ❌ **FAILED — 500** *(REGRESSION — was working earlier today)* |
| 4 | `POST /v2/documents?subAccountId={id}` | Create doc upload session | ✅ **SUCCESS** (200) |
| 5 | `PUT <S3 pre-signed URL>` | Upload binary to S3 | ✅ **SUCCESS** (200) |
| 6 | `GET /v2/documents/{id}?subAccountId={id}` | Poll document `ready` | ✅ **SUCCESS** (200) |
| 7 | `POST /v2/account/ubos?subAccountId={id}` | Create UBO | ✅ **SUCCESS** (201) — *fixed by support today* |
| 8 | `GET /v2/account/ubos?subAccountId={id}` | List UBOs | ❌ **FAILED — 500** |
| 9 | `POST /v2/kyc/new-level-1/api?subAccountId={id}` | Submit KYB Level 1 | ❌ **FAILED — 500** |
| 10 | `GET /v2/kyc/attempts/{id}?subAccountId={id}` | Poll KYB Level 1 status | ⚠️ **BLOCKED** (no attemptId because Step 9 fails) |
| 11 | `POST /v2/account/proof-of-financial-capacity/api?subAccountId={id}` | Submit Proof of Financial Capacity | ❌ **FAILED — 500** |
| 12 | `GET /v2/account/proof-of-financial-capacity/attempts/{id}?subAccountId={id}` | Poll PoFC status | ⚠️ **BLOCKED** (no attemptId because Step 11 fails) |
| 13 | `POST /v2/kyc/usd/api?subAccountId={id}` | Submit KYB USD | ⚠️ **BLOCKED** — validates body correctly (400 "missing required documents") but cannot reach 201 because Steps 9 and 11 never link the prerequisite documents |

**Net result:** of 13 endpoints in the documented flow, **6 work, 4 are broken (500 InternalError), 3 are blocked downstream.**

---

## Working endpoints — proof of correct integration

### 1. GET /v2/account/account-info — ✅ SUCCESS

**Request:** (no body)

**Response 200:**
```json
{
  "id": "4ffdf191-2ada-453d-9a66-35841ea1cc5d",
  "accountInfo": {
    "id": "58e76382-41ae-42a9-bd86-2cf33569caac",
    "accountType": "COMPANY",
    "identityStatus": "NOT-IDENTIFIED",
    ...
  },
  "wallets": [...],
  "pixKey": "7bf464cf-4e18-40f7-b33e-d2b1ef14ab73",
  ...
}
```

### 2. GET /v2/account/sub-accounts — ✅ SUCCESS

Returns list of all subaccounts under our account (200 OK with cursor + array).

### 3. POST /v2/documents — ✅ SUCCESS (all 5 document types)

**Request:**
```json
{
  "documentType": "CERTIFICATE-OF-INCORPORATION",
  "isDoubleSided": false
}
```

**Response 200:**
```json
{
  "id": "4bc63f09-f579-4c0f-99b7-87a0c0feba13",
  "uploadURLFront": "https://s3.eu-west-1.amazonaws.com/...",
  "uploadURLBack": ""
}
```

Verified for all 5 required types: `CERTIFICATE-OF-INCORPORATION`, `COMPANY-TAX-IDENTIFICATION-DOCUMENT`, `PASSPORT` (UBO ID), `PROOF-OF-FINANCIAL-CAPACITY`, `PROOF-OF-REVENUE`.

### 4. PUT to S3 pre-signed URL — ✅ SUCCESS

Direct PUT of binary bytes with `Content-Type: application/pdf` (or `image/png`) and `If-None-Match: *`. Returns 200/204.

### 5. GET /v2/documents/{id} — ✅ SUCCESS (polling)

**Response when ready:**
```json
{
  "document": {
    "id": "4bc63f09-f579-4c0f-99b7-87a0c0feba13",
    "documentType": "CERTIFICATE-OF-INCORPORATION",
    "uploadStatusFront": "OK",
    "ready": true,
    ...
  }
}
```

All 5 documents reach `ready: true` within ~10 seconds.

### 6. POST /v2/account/ubos — ✅ SUCCESS (after support fix earlier today)

**Request:**
```json
{
  "fullName": "CARLOS MENDES FERREIRA",
  "dateOfBirth": "1988-07-22",
  "countryOfTaxId": "BRA",
  "taxIdNumber": "66696833161",
  "email": "carlos.ferreira@nexoradigital.com.br",
  "phone": "+5511987654321",
  "percentageOfOwnership": "100",
  "hasControl": "CEO",
  "uploadedIdentificationId": "588231fb-2b7f-4364-8c9d-b4b300610175",
  "documentCountry": "BRA",
  "streetLine1": "RUA AURORA 456 APTO 12",
  "streetLine2": "",
  "streetLine3": "",
  "city": "SAO PAULO",
  "state": "SP",
  "zipCode": "01209-001",
  "country": "BRA"
}
```

**Response 201:**
```json
{
  "id": "51a50417-932a-45f4-b0e5-11478fcef3b6"
}
```

> ℹ️ **Doc note for support:** The KYB API guide shows `"percentageOfOwnership": 100` as a number, but the actual API rejects numbers with `400 "InvalidBodyError: could not parse body data"`. Only the string form `"100"` is accepted. Please update your docs.

---

## Broken endpoints — all returning 500 InternalError "please contact us"

### ❌ POST /v2/account/sub-accounts — FAILED (REGRESSION)

> ⚠️ **This was working earlier today** (2026-04-29 ~10:34 UTC we successfully created subaccount `e08c1f3d-fc31-4c04-8bf0-fab528f57cb1`). It started failing again on 2026-04-30.

**Request:**
```json
{
  "accountType": "COMPANY",
  "name": "Nexora Digital Solutions Ltd - POC 20260430042758"
}
```

**Response 500:**
```json
{ "error": "InternalError: please contact us" }
```

Reproduced consistently across multiple retries. **Currently blocking ALL fresh KYB tests** — we cannot create a new subaccount.

### ❌ GET /v2/account/ubos — FAILED

**Request:** `GET /v2/account/ubos?subAccountId=e08c1f3d-fc31-4c04-8bf0-fab528f57cb1` (no body)

**Response 500:**
```json
{ "error": "InternalError: please contact us" }
```

GET on this endpoint never works — same error every time. (POST works.)

### ❌ POST /v2/kyc/new-level-1/api — FAILED

**Request:**
```json
{
  "uboIds": ["51a50417-932a-45f4-b0e5-11478fcef3b6"],
  "companyLegalName": "NEXORA SOLUCOES DIGITAIS LTDA",
  "companyRegistrationNumber": "01760883000109",
  "taxIdentificationNumberTin": "01.760.883/0001-09",
  "businessActivityDescription": "Custom software development and IT consulting services. The company provides SaaS solutions for financial institutions.",
  "website": "https://www.nexoradigital.com.br",
  "businessModel": "Software house that develops solutions for financial institutions",
  "socialMedia": "https://linkedin.com/company/nexoradigital",
  "reasonForAccountOpening": "receive_payments_for_goods_and_services",
  "sourceOfFundsAndIncome": "sales_of_goods_and_services",
  "numberOfEmployees": "1-10",
  "estimatedAnnualRevenueUsd": "less_than_100k",
  "estimatedMonthlyVolumeUsd": "2000",
  "countryTaxResidence": "BRA",
  "countrySubdivisionTaxResidence": "BR-SP",
  "companyStreetLine1": "AV PAULISTA 1000 CONJ 204",
  "companyStreetLine2": "",
  "companyStreetLine3": "",
  "companyCity": "SAO PAULO",
  "companyState": "SP",
  "companyZipCode": "01310-100",
  "companyCountry": "Brazil",
  "certificateOfIncorporationDocumentId": "4bc63f09-f579-4c0f-99b7-87a0c0feba13",
  "taxIdentificationDocumentId": "4b850e07-773e-49e8-9f32-23d93390b540"
}
```

**Response 500:**
```json
{ "error": "InternalError: please contact us" }
```

Reproduced with multiple valid CNPJs (algorithmically valid check digits). **Same kind of bug the UBO endpoint had** before support fixed it earlier today.

> ⚠️ **Side effect:** despite returning 500, the request appears to *partially commit* — subsequent UBO POSTs to the same subaccount return `409 Conflict: "KYB data is immutable after approval"`, which is misleading because the docs were never actually linked (proven by Step 13 below).

### ❌ POST /v2/account/proof-of-financial-capacity/api — FAILED

**Request:**
```json
{
  "uploadedPoFCId": "e7568492-61aa-4728-b73e-eb0fd82ece98"
}
```

**Response 500:**
```json
{ "error": "InternalError: please contact us" }
```

Same generic 500 — same pattern as KYB Level 1.

---

## Blocked downstream — proves the upstream 500s never actually completed

### ⚠️ POST /v2/kyc/usd/api — BLOCKED

**Request:**
```json
{
  "businessType": "llc",
  "businessIndustries": ["519290"],
  "proofOfRevenueDocId": "a2192f3b-f262-4b36-b12b-764349e4686c",
  "certificateOfIncorporationDocId": "4bc63f09-f579-4c0f-99b7-87a0c0feba13",
  "proofOfFinancialCapacityDocId": "e7568492-61aa-4728-b73e-eb0fd82ece98"
}
```

**Response 400:**
```json
{
  "error": "missing required documents",
  "extraInfo": "certificate of incorporation; proof of financial capacity"
}
```

This endpoint validates the body correctly. The error confirms that **Steps 9 (KYB Level 1) and 11 (PoFC) never actually linked their documents server-side** — even though both POSTs returned 500, the docs are not associated with the subaccount, so KYB USD can't proceed.

> ℹ️ **Doc gap:** the public docs only list `businessType`, `businessIndustries`, `proofOfRevenueDocId` for this endpoint. The error implies `certificateOfIncorporationDocId` and `proofOfFinancialCapacityDocId` are also expected (or the prerequisite linking must happen via the upstream calls). Please clarify in the docs.

---

## Concrete asks for Avenia support

1. **Restore `POST /v2/account/sub-accounts`** — currently 500ing; it was working at ~2026-04-29 10:34 UTC and broke between then and 2026-04-30 04:27 UTC.
2. **Fix `POST /v2/kyc/new-level-1/api`** — same generic 500 pattern as the UBO endpoint that was fixed today; please apply the same fix.
3. **Fix `POST /v2/account/proof-of-financial-capacity/api`** — same generic 500 pattern.
4. **Fix `GET /v2/account/ubos`** — 500s consistently.
5. **Reset / clean up our stuck subaccount** `e08c1f3d-fc31-4c04-8bf0-fab528f57cb1` — it is in an inconsistent half-approved state due to the partial commits from the broken endpoints. We can't progress and can't reset.
6. **Documentation fixes:**
   - `percentageOfOwnership` is documented as a JSON number (`100`) but the API requires a JSON string (`"100"`)
   - The example CPF `37482159603` in the UBO docs has invalid check digits — reject from any future docs
   - `POST /v2/kyc/usd/api` requires `certificateOfIncorporationDocId` + `proofOfFinancialCapacityDocId` (or document them as required prerequisites linked via the upstream steps)

## Once the four broken endpoints are fixed, no client-side changes are needed
Our integration code is complete and matches the (corrected) API contract. The full flow — create subaccount → upload 5 docs → create UBO → KYB Level 1 → PoFC → KYB USD APPROVED — should run end-to-end with zero further changes.

---

## Reference IDs from current testing

- Stuck subaccount: `e08c1f3d-fc31-4c04-8bf0-fab528f57cb1`
- Created UBO on stuck subaccount: `51a50417-932a-45f4-b0e5-11478fcef3b6` (and `98132778-a089-4e56-a042-281791b0313b` from an earlier successful UBO POST)
- Document IDs on stuck subaccount:
  - Certificate of Incorporation: `4bc63f09-f579-4c0f-99b7-87a0c0feba13`
  - Company Tax ID: `4b850e07-773e-49e8-9f32-23d93390b540`
  - UBO ID (PASSPORT): `588231fb-2b7f-4364-8c9d-b4b300610175`
  - Proof of Financial Capacity: `e7568492-61aa-4728-b73e-eb0fd82ece98`
  - Proof of Revenue: `a2192f3b-f262-4b36-b12b-764349e4686c`
