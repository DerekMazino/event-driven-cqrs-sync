SET SERVEROUTPUT ON;
SET TIMING ON;
SET ECHO OFF;
SET FEEDBACK OFF;

ALTER SESSION SET CONTAINER = FREEPDB1;
ALTER SESSION SET CURRENT_SCHEMA = CQRS_USER;

PROMPT ========================================================
PROMPT [BENCHMARK] Sembrando clientes, prestamos y cuotas (3NF)...
PROMPT ========================================================

DECLARE
    v_cust_count NUMBER;
BEGIN
    -- 1. Insertar 5,000 clientes solo si no se han cargado aun
    SELECT COUNT(*) INTO v_cust_count FROM customers;
    IF v_cust_count < 5000 THEN
        INSERT /*+ APPEND */ INTO customers (tax_id, full_name, email)
        SELECT 
            'TX-' || LPAD(TO_CHAR(LEVEL + 10000), 10, '0'),
            'Customer ' || LEVEL,
            'client_' || LEVEL || '@benchmark.io'
        FROM dual
        CONNECT BY LEVEL <= (5000 - v_cust_count);
        COMMIT;
    END IF;

    -- 2. Generar 50,000 préstamos vinculados directamente a los customer_id existentes (10 prestamos por cada uno de los 5,000 clientes)
    INSERT /*+ APPEND */ INTO loan_applications (customer_id, loan_amount, interest_rate, term_months, status, approved_at)
    SELECT 
        c.customer_id,
        ROUND(DBMS_RANDOM.VALUE(5000, 75000), 2),
        ROUND(DBMS_RANDOM.VALUE(8, 24), 2),
        12,
        'APPROVED',
        SYSTIMESTAMP - NUMTODSINTERVAL(DBMS_RANDOM.VALUE(1, 365), 'DAY')
    FROM (SELECT customer_id FROM customers WHERE ROWNUM <= 5000) c
    CROSS JOIN (SELECT LEVEL AS rep FROM dual CONNECT BY LEVEL <= 10);
    COMMIT;

    -- 3. Generar las 12 cuotas de amortización para cada préstamo recién creado (600,000 cuotas)
    INSERT /*+ APPEND */ INTO payment_schedules (loan_id, installment_no, due_date, principal_due, interest_due, total_due, payment_status)
    SELECT 
        l.loan_id,
        s.lvl,
        ADD_MONTHS(TRUNC(l.approved_at), s.lvl),
        ROUND(l.loan_amount / l.term_months, 2),
        ROUND((l.loan_amount * (l.interest_rate / 100)) / l.term_months, 2),
        ROUND((l.loan_amount / l.term_months) + ((l.loan_amount * (l.interest_rate / 100)) / l.term_months), 2),
        CASE WHEN DBMS_RANDOM.VALUE(0, 1) < 0.6 THEN 'PAID' ELSE 'PENDING' END
    FROM loan_applications l
    CROSS JOIN (SELECT LEVEL AS lvl FROM dual CONNECT BY LEVEL <= 12) s
    WHERE NOT EXISTS (
        SELECT 1 FROM payment_schedules ps WHERE ps.loan_id = l.loan_id
    );
    COMMIT;

    -- 4. Actualizar estadísticas del optimizador (CBO)
    DBMS_STATS.GATHER_SCHEMA_STATS('CQRS_USER', cascade => TRUE);
    DBMS_OUTPUT.PUT_LINE('--- Poblacion completada con exito ---');
END;
/
EXIT;
