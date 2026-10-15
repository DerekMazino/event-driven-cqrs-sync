(() => {
    const dbInstance = db.getSiblingDB("loan_read_model");

    print("========================================================");
    print("[BENCHMARK MONGODB] Consulta sobre documento desnormalizado");
    print("========================================================");

    const exp = dbInstance.loans_summary
        .find({ status: "APPROVED" })
        .limit(100)
        .explain("executionStats");

    const stats = exp.executionStats || {};

    print("Tiempo de Ejecucion (ms) : " + (stats.executionTimeMillis ?? 0) + " ms");
    print("Documentos Escaneados    : " + (stats.totalDocsExamined ?? 0));
    print("Documentos Retornados    : " + (stats.nReturned ?? 0));
})();
