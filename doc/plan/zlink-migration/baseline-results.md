# Zlink Migration Baseline Results

- 작성일: 2026-02-12
- 기록 폴더: `doc/plan/zlink-migration`
- Raw 결과 보관 루트: `doc/plan/zlink-migration/results/<timestamp>/`

## 기록 규칙

- 모든 baseline은 CS/SS 모두 측정한다.
- 메시지 크기 `64`, `1024`, `65536`을 모두 측정한다.
- 각 측정 실행 후 결과 파일(raw)을 `doc/plan/zlink-migration/results/<timestamp>/`에 보관한다.
- 요약 수치는 아래 표에 추가한다.

## CS Baseline Summary

| Timestamp | Size | CCU | Inflight | Mode | Throughput | Avg Latency | P95 Latency | Error Rate | Raw Path |
|---|---:|---:|---:|---|---:|---:|---:|---:|---|
| TBD | 64 | 10000 | 30 | send | TBD | TBD | TBD | TBD | `doc/plan/zlink-migration/results/<timestamp>/cs/raw` |
| TBD | 1024 | 10000 | 30 | send | TBD | TBD | TBD | TBD | `doc/plan/zlink-migration/results/<timestamp>/cs/raw` |
| TBD | 65536 | 10000 | 30 | send | TBD | TBD | TBD | TBD | `doc/plan/zlink-migration/results/<timestamp>/cs/raw` |

## SS Baseline Summary

| Timestamp | Size | CCU | Inflight | Mode | Throughput | Avg Latency | P95 Latency | Error Rate | Raw Path |
|---|---:|---:|---:|---|---:|---:|---:|---:|---|
| TBD | 64 | 10000 | 10 | send | TBD | TBD | TBD | TBD | `doc/plan/zlink-migration/results/<timestamp>/ss/raw` |
| TBD | 1024 | 10000 | 10 | send | TBD | TBD | TBD | TBD | `doc/plan/zlink-migration/results/<timestamp>/ss/raw` |
| TBD | 65536 | 10000 | 10 | send | TBD | TBD | TBD | TBD | `doc/plan/zlink-migration/results/<timestamp>/ss/raw` |

## Run History

- `20260212_153036` (CS/SS baseline, size 64/1024/65536, mode send)
- `20260212_155407` (Post-Zlink, CS/SS gate run, size 64/1024/65536, mode send)

## 20260212_153036

- Raw archive: `doc/plan/zlink-migration/results/20260212_153036`
- CS raw: `doc/plan/zlink-migration/results/20260212_153036/cs/raw`
- SS raw: `doc/plan/zlink-migration/results/20260212_153036/ss/raw`
- SS note: `PlayHouse.Benchmark.SS.Client`는 현재 결과 파일 미저장이라 콘솔 결과를 `ss_console_summary.json`으로 기록

### CS Measured

| Timestamp | Size | CCU | Inflight | Mode | Throughput | Avg Latency | P95 Latency | Error Rate | Raw Path |
|---|---:|---:|---:|---|---:|---:|---:|---:|---|
| 2026-02-12 15:30:54 | 64 | 10000 | 30 | send | 488,324.56 | 427.03 | 741.84 | 0.000% | `doc/plan/zlink-migration/results/20260212_153036/cs/raw` |
| 2026-02-12 15:31:29 | 1024 | 10000 | 30 | send | 511,596.87 | 314.54 | 1208.05 | 0.000% | `doc/plan/zlink-migration/results/20260212_153036/cs/raw` |
| 2026-02-12 15:32:06 | 65536 | 10000 | 30 | send | 17,954.15 | 1154.37 | 4991.64 | 31.823% | `doc/plan/zlink-migration/results/20260212_153036/cs/raw` |

### SS Measured

| Timestamp | Size | CCU | Inflight | Mode | Throughput | Avg Latency | P95 Latency | Error Rate | Raw Path |
|---|---:|---:|---:|---|---:|---:|---:|---:|---|
| 2026-02-12 15:33:47 | 64 | 10000 | 10 | send | 500,661 | 330.58 | 588.37 | N/A | `doc/plan/zlink-migration/results/20260212_153036/ss/raw/ss_console_summary.json` |
| 2026-02-12 15:34:21 | 1024 | 10000 | 10 | send | 370,190 | 1069.46 | 1978.64 | N/A | `doc/plan/zlink-migration/results/20260212_153036/ss/raw/ss_console_summary.json` |
| 2026-02-12 15:34:57 | 65536 | 10000 | 10 | send | 33,230 | 5065.60 | 10036.45 | N/A | `doc/plan/zlink-migration/results/20260212_153036/ss/raw/ss_console_summary.json` |

## 20260212_155407 (Post-Zlink)

- Raw archive: `doc/plan/zlink-migration/results/20260212_155407`
- CS raw: `doc/plan/zlink-migration/results/20260212_155407/cs/raw`
- SS raw: `doc/plan/zlink-migration/results/20260212_155407/ss/raw`
- SS note: `PlayHouse.Benchmark.SS.Client`는 결과 파일 미저장이라 콘솔 결과를 `ss_console_summary.json`으로 기록
- SS note: size 64 재실행 로그 `ss_size64_rerun.console.log`에서도 `0 msg/s` 재현

### CS Measured (Post-Zlink)

| Timestamp | Size | CCU | Inflight | Mode | Throughput | Avg Latency | P95 Latency | Error Rate | Raw Path |
|---|---:|---:|---:|---|---:|---:|---:|---:|---|
| 2026-02-12 15:54:17 | 64 | 10000 | 30 | send | 487,010.72 | 428.44 | 653.89 | 0.000% | `doc/plan/zlink-migration/results/20260212_155407/cs/raw` |
| 2026-02-12 15:54:45 | 1024 | 10000 | 30 | send | 474,448.74 | 440.05 | 1338.60 | 0.000% | `doc/plan/zlink-migration/results/20260212_155407/cs/raw` |
| 2026-02-12 15:55:16 | 65536 | 10000 | 30 | send | 20,234.21 | 1925.90 | 6581.53 | 24.073% | `doc/plan/zlink-migration/results/20260212_155407/cs/raw` |

### SS Measured (Post-Zlink)

| Timestamp | Size | CCU | Inflight | Mode | Throughput | Avg Latency | P95 Latency | Error Rate | Raw Path |
|---|---:|---:|---:|---|---:|---:|---:|---:|---|
| 2026-02-12 15:57:05 | 64 | 10000 | 10 | send | 0 | 0.00 | 0.00 | N/A | `doc/plan/zlink-migration/results/20260212_155407/ss/raw/ss_console_summary.json` |
| 2026-02-12 15:57:34 | 1024 | 10000 | 10 | send | 0 | 0.00 | 0.00 | N/A | `doc/plan/zlink-migration/results/20260212_155407/ss/raw/ss_console_summary.json` |
| 2026-02-12 15:58:06 | 65536 | 10000 | 10 | send | 18,906 | 5504.83 | 11327.94 | N/A | `doc/plan/zlink-migration/results/20260212_155407/ss/raw/ss_console_summary.json` |

### Baseline Comparison (20260212_153036 -> 20260212_155407)

| Size | CS Throughput (Base) | CS Throughput (Post) | Delta | CS Avg Lat (Base) | CS Avg Lat (Post) | Delta | CS P95 (Base) | CS P95 (Post) | Delta | CS Error (Base) | CS Error (Post) | Delta |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 64 | 488,324.56 | 487,010.72 | -0.27% | 427.03 | 428.44 | +0.33% | 741.84 | 653.89 | -11.86% | 0.000% | 0.000% | +0.000pp |
| 1024 | 511,596.87 | 474,448.74 | -7.26% | 314.54 | 440.05 | +39.90% | 1208.05 | 1338.60 | +10.81% | 0.000% | 0.000% | +0.000pp |
| 65536 | 17,954.15 | 20,234.21 | +12.70% | 1154.37 | 1925.90 | +66.84% | 4991.64 | 6581.53 | +31.85% | 31.823% | 24.073% | -7.750pp |

| Size | SS Throughput (Base) | SS Throughput (Post) | Delta | SS Avg Lat (Base) | SS Avg Lat (Post) | Delta | SS P95 (Base) | SS P95 (Post) | Delta |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 64 | 500,661 | 0 | -100.00% | 330.58 | 0.00 | -100.00% | 588.37 | 0.00 | -100.00% |
| 1024 | 370,190 | 0 | -100.00% | 1069.46 | 0.00 | -100.00% | 1978.64 | 0.00 | -100.00% |
| 65536 | 33,230 | 18,906 | -43.11% | 5065.60 | 5504.83 | +8.67% | 10036.45 | 11327.94 | +12.87% |

### Observation

- CS는 64B에서 baseline과 유사하나, 1024B/65536B에서 지연이 유의미하게 증가.
- SS는 64B/1024B가 0 msg/s로 측정되어 성능 게이트 기준에서 즉시 No-Go.
- SS 64B 재실행(2026-02-12 15:59:31)에서도 0 msg/s 재현됨.
