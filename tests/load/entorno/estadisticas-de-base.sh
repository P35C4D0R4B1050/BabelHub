#!/usr/bin/env bash
# Estadísticas de PostgreSQL de la corrida (DOC-1): consultas más costosas, uso de índices, lecturas secuenciales, bloqueos y acierto de caché del búfer.
# Uso: estadisticas-de-base.sh reiniciar   (antes de la corrida)   |   estadisticas-de-base.sh volcar <archivo>   (después)
set -euo pipefail
CARGA_CARPETA="${CARGA_CARPETA:-$(cygpath -m "$(cd "$(git rev-parse --git-common-dir)/../.." && pwd)")/babelhub-carga}"
set -a; source "$CARGA_CARPETA/env"; set +a
psql_() { MSYS_NO_PATHCONV=1 docker exec -i "${CARGA_PROYECTO:-bhcarga}-postgres-1" psql -U "$POSTGRES_ADMIN_USER" -d inventario "$@"; }
case "${1:?reiniciar|volcar}" in
  reiniciar) psql_ -qAt -c "SELECT pg_stat_statements_reset(); SELECT pg_stat_reset();" >/dev/null ;;
  volcar)
    psql_ -P pager=off <<'SQL' > "${2:?archivo}"
\echo '== Consultas más costosas (tiempo total) =='
SELECT calls, round(total_exec_time::numeric,1) AS total_ms, round(mean_exec_time::numeric,3) AS media_ms, round(max_exec_time::numeric,1) AS max_ms, rows,
       left(regexp_replace(query, '\s+', ' ', 'g'), 150) AS consulta
FROM pg_stat_statements WHERE query NOT ILIKE '%pg_stat%' ORDER BY total_exec_time DESC LIMIT 15;
\echo '== Uso de índices por tabla (lecturas por índice frente a secuenciales) =='
SELECT schemaname||'.'||relname AS tabla, seq_scan, idx_scan, n_live_tup AS filas FROM pg_stat_user_tables WHERE seq_scan + idx_scan > 0 ORDER BY seq_scan + idx_scan DESC LIMIT 15;
\echo '== Índices más usados =='
SELECT schemaname||'.'||indexrelname AS indice, idx_scan FROM pg_stat_user_indexes WHERE idx_scan > 0 ORDER BY idx_scan DESC LIMIT 12;
\echo '== Bloqueos e interbloqueos de la base =='
SELECT deadlocks, conflicts, temp_files, xact_commit, xact_rollback, round(100.0*blks_hit/NULLIF(blks_hit+blks_read,0),2) AS acierto_de_cache_pct FROM pg_stat_database WHERE datname = 'inventario';
\echo '== Conexiones por cuenta (límite 20 por cuenta) =='
SELECT usename, count(*) AS conexiones FROM pg_stat_activity WHERE datname='inventario' GROUP BY usename ORDER BY 2 DESC;
SQL
    ;;
esac
