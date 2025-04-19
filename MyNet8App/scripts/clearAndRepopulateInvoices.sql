USE XeroPOC;
GO

-- 1. Disable versioning on Invoices
ALTER TABLE dbo.XeroODS_Invoices
  SET (SYSTEM_VERSIONING = OFF);
GO

-- 2. Truncate Invoices & its history
TRUNCATE TABLE dbo.XeroODS_Invoices;
TRUNCATE TABLE dbo.XeroODS_Invoices_History;
GO

-- 3. Re‑enable versioning on Invoices
ALTER TABLE dbo.XeroODS_Invoices
  SET (
    SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.XeroODS_Invoices_History)
  );
GO

-- 4. Disable versioning on InvoiceLines
ALTER TABLE dbo.XeroODS_InvoiceLines
  SET (SYSTEM_VERSIONING = OFF);
GO

-- 5. Truncate InvoiceLines & its history
TRUNCATE TABLE dbo.XeroODS_InvoiceLines;
TRUNCATE TABLE dbo.XeroODS_InvoiceLines_History;
GO

-- 6. Re‑enable versioning on InvoiceLines
ALTER TABLE dbo.XeroODS_InvoiceLines
  SET (
    SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.XeroODS_InvoiceLines_History)
  );
GO

-- 7. Reset flags on raw JSON so everything gets re‑processed
UPDATE dbo.XeroRaw_Invoices
SET
  Processed     = 0,
  LineProcessed = 0;
GO

-- 8. Re‑run the invoice‑header shred proc
EXEC dbo.ProcessInvoicesRaw;
GO

-- 9. Re‑run the invoice‑lines shred proc
EXEC dbo.ProcessInvoiceLinesRaw;
GO

-- 10. Verify quickly
SELECT
  (SELECT COUNT(*) FROM dbo.XeroODS_Invoices)       AS [InvoiceRows],
  (SELECT COUNT(*) FROM dbo.XeroODS_Invoices_History) AS [InvoiceHistoryRows],
  (SELECT COUNT(*) FROM dbo.XeroODS_InvoiceLines)   AS [InvoiceLineRows],
  (SELECT COUNT(*) FROM dbo.XeroODS_InvoiceLines_History) AS [InvoiceLineHistoryRows],
  (SELECT COUNT(*) FROM dbo.XeroRaw_Invoices WHERE Processed = 1)       AS [HeadersProcessed],
  (SELECT COUNT(*) FROM dbo.XeroRaw_Invoices WHERE LineProcessed = 1)   AS [LinesProcessed];
GO
