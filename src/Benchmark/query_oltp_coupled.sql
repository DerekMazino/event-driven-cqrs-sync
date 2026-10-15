SET ECHO OFF;
SET FEEDBACK OFF;
SET TIMING ON;
SET LINESIZE 250;
SET PAGESIZE 1000;
SET TRIMOUT ON;
SET TRIMSPOOL ON;

ALTER SESSION SET CONTAINER = FREEPDB1;
ALTER SESSION SET CURRENT_SCHEMA = CQRS_USER;
ALTER SESSION SET STATISTICS_LEVEL = ALL;

PROMPT ========================================================
PROMPT [BENCHMARK OLTP] Consulta con JOINs de 3 entidades y agregacion
PROMPT ========================================================

SELECT /*+ GATHER_PLAN_STATISTICS */
    c.tax_id,
    c.full_name,
    l.loan_id,
    l.loan_amount,
    l.interest_rate,
    COUNT(s.schedule_id) AS total_installments,
    SUM(CASE WHEN s.payment_status = 'PAID' THEN s.total_due ELSE 0 END) AS total_paid,
    SUM(CASE WHEN s.payment_status = 'PENDING' THEN s.total_due ELSE 0 END) AS total_outstanding
FROM customers c
JOIN loan_applications l ON c.customer_id = l.customer_id
JOIN payment_schedules s ON l.loan_id = s.loan_id
WHERE l.status = 'APPROVED'
GROUP BY c.tax_id, c.full_name, l.loan_id, l.loan_amount, l.interest_rate
FETCH FIRST 100 ROWS ONLY;

PROMPT
PROMPT ========================================================
PROMPT [PLAN DE EJECUCION OLTP]
PROMPT ========================================================

SELECT * FROM TABLE(DBMS_XPLAN.DISPLAY_CURSOR(NULL, NULL, 'ALLSTATS LAST +COST +BYTES'));

EXIT;
