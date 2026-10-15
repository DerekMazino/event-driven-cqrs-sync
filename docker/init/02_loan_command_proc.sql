ALTER SESSION SET CONTAINER = FREEPDB1;
ALTER SESSION SET CURRENT_SCHEMA = CQRS_USER;

CREATE OR REPLACE PROCEDURE sp_create_loan (
    p_customer_id   IN NUMBER,
    p_amount        IN NUMBER,
    p_interest_rate IN NUMBER,
    p_term_months   IN NUMBER,
    p_out_loan_id   OUT NUMBER
) AS
    v_loan_id        NUMBER;
    v_principal_part NUMBER(15, 2);
    v_interest_part  NUMBER(15, 2);
    v_monthly_total  NUMBER(15, 2);
    v_event_payload  CLOB;
BEGIN
    -- 1. Insertar el encabezado del préstamo
    INSERT INTO loan_applications (customer_id, loan_amount, interest_rate, term_months, status)
    VALUES (p_customer_id, p_amount, p_interest_rate, p_term_months, 'APPROVED')
    RETURNING loan_id INTO v_loan_id;

    p_out_loan_id := v_loan_id;

    -- 2. Calcular cuotas fijas básicas y poblar la tabla de amortización (3NF)
    v_principal_part := ROUND(p_amount / p_term_months, 2);
    v_interest_part  := ROUND((p_amount * (p_interest_rate / 100)) / p_term_months, 2);
    v_monthly_total  := v_principal_part + v_interest_part;

    FOR i IN 1..p_term_months LOOP
        INSERT INTO payment_schedules (
            loan_id, installment_no, due_date, principal_due, interest_due, total_due, payment_status
        ) VALUES (
            v_loan_id, i, ADD_MONTHS(TRUNC(SYSDATE), i), v_principal_part, v_interest_part, v_monthly_total, 'PENDING'
        );
    END LOOP;

    -- 3. Construir el JSON del evento para el Outbox
    v_event_payload := JSON_OBJECT(
        'loanId' VALUE v_loan_id,
        'customerId' VALUE p_customer_id,
        'amount' VALUE p_amount,
        'interestRate' VALUE p_interest_rate,
        'termMonths' VALUE p_term_months,
        'monthlyInstallment' VALUE v_monthly_total,
        'status' VALUE 'APPROVED',
        'occurredOn' VALUE TO_CHAR(SYSTIMESTAMP, 'YYYY-MM-DD"T"HH24:MI:SS.FF3"Z"')
    );

    -- 4. Registrar en el Transactional Outbox (ATÓMICO: Dentro de la misma transacción)
    INSERT INTO transactional_outbox (aggregate_type, aggregate_id, event_type, payload, status)
    VALUES ('LOAN', TO_CHAR(v_loan_id), 'LoanCreatedEvent', v_event_payload, 'PENDING');

    COMMIT;
END;
/