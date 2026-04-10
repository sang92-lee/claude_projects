#!/usr/bin/env bash
# =============================================================================
# server-check.sh  —  /etc/hosts 서버들의 상태/CPU/상위 프로세스 조회
#
# 사용법:
#   export SSH_USER=admin SSH_PASS='mypassword'
#   ./server-check.sh
#
# 환경변수 (선택):
#   SSH_PORT         기본값: 22
#   CONNECT_TIMEOUT  접속 타임아웃 (초, 기본값: 5)
#   CMD_TIMEOUT      명령 타임아웃 (초, 기본값: 20)
#
# 의존성: bash >= 4, expect, ssh
# =============================================================================

SSH_USER="${SSH_USER:-root}"
SSH_PASS="${SSH_PASS:-}"
SSH_PORT="${SSH_PORT:-22}"
CONNECT_TIMEOUT="${CONNECT_TIMEOUT:-5}"
CMD_TIMEOUT="${CMD_TIMEOUT:-20}"

# ── ANSI 색상 (터미널 출력 시에만 활성화) ────────────────────────────────────
if [[ -t 1 ]]; then
  RED='\033[0;31m' YEL='\033[1;33m' GRN='\033[0;32m'
  CYN='\033[0;36m' BLD='\033[1m'    RST='\033[0m'
else
  RED='' YEL='' GRN='' CYN='' BLD='' RST=''
fi

# ── 원격 실행 스크립트 (base64 인코딩 → SSH 명령 인수 따옴표 문제 회피) ──────
# shellcheck disable=SC2016
REMOTE_B64=$(base64 << 'REMOTE_EOF' | tr -d '\n'
#!/bin/bash
# /proc/stat 두 번 읽어 정확한 현재 CPU 사용률 계산 (약 0.5초 소요)
_c1=$(awk 'NR==1{for(i=2;i<=NF;i++) printf $i" "}' /proc/stat 2>/dev/null)
sleep 0.5
_c2=$(awk 'NR==1{for(i=2;i<=NF;i++) printf $i" "}' /proc/stat 2>/dev/null)
CPU=$(awk -v a="$_c1" -v b="$_c2" 'BEGIN{
    n=split(a,x," "); split(b,y," ")
    t1=0; t2=0
    for(i=1;i<=n;i++){ t1+=x[i]; t2+=y[i] }
    dt=t2-t1; idle=y[4]-x[4]
    printf "%.1f", (dt>0) ? 100*(dt-idle)/dt : 0
}')
# CPU 사용률 상위 5개 프로세스
PROCS=$(ps aux --sort=-%cpu 2>/dev/null | awk '
    NR == 1 { next }
    ++cnt > 5 { exit }
    {
        cmd = $11
        sub(/.*\//, "", cmd)        # 경로 제거
        sub(/^-/, "", cmd)          # 로그인 셸 대시 제거
        sub(/\(.*\)$/, "", cmd)     # 괄호 인수 제거
        if (length(cmd) > 14) cmd = substr(cmd,1,13)"+"
        printf "%s:%.1f ", cmd, $3
    }
' | sed 's/[[:space:]]*$//')
printf "##RESULT|%s|%s##\n" "${CPU:-N/A}" "${PROCS:--}"
REMOTE_EOF
)

# ── /etc/hosts 파싱 ───────────────────────────────────────────────────────────
parse_hosts() {
    awk '
        /^[[:space:]]*#/  { next }    # 주석
        /^[[:space:]]*$/  { next }    # 빈 줄
        /\<localhost\>/   { next }    # localhost
        /broadcasthost/   { next }
        /ip6-/            { next }    # IPv6 호스트명
        /^127\./          { next }    # 루프백
        /^::1[[:space:]]/ { next }    # IPv6 루프백
        /^0\.0\.0\.0/     { next }    # null route
        /^255\./          { next }    # 브로드캐스트
        /^fe80:/          { next }    # 링크 로컬 IPv6
        NF >= 2 && $1 ~ /^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$/ {
            print $1, $2
        }
    ' /etc/hosts
}

# ── expect 로 서버 한 대 체크 ─────────────────────────────────────────────────
# SSH_PASS는 환경변수로 전달해 expect 스크립트 내 특수문자 문제 방지
check_server() {
    local ip="$1" name="$2"
    local expfile
    expfile=$(mktemp /tmp/.sc_XXXXXX.exp)

    cat > "$expfile" << EXPECT_SCRIPT
log_user 0
set timeout $CMD_TIMEOUT
set pass \$env(SSH_PASS)

spawn ssh \
    -o StrictHostKeyChecking=no \
    -o UserKnownHostsFile=/dev/null \
    -o LogLevel=ERROR \
    -o ConnectTimeout=$CONNECT_TIMEOUT \
    -o BatchMode=no \
    -p $SSH_PORT \
    ${SSH_USER}@${ip} \
    {echo '$REMOTE_B64' | base64 -d | bash 2>/dev/null}

set auth_tried 0
expect {
    -re {[Pp]assword[^:]*:[[:space:]]*$} {
        if { \$auth_tried < 1 } {
            send "\$pass\r"
            incr auth_tried
            exp_continue
        } else {
            puts {ERR|AUTH_FAIL}
        }
    }
    -re {yes/no} {
        send "yes\r"
        exp_continue
    }
    {Permission denied}   { puts {ERR|AUTH_FAIL}   }
    {Connection refused}  { puts {ERR|CONN_REFUSED} }
    {No route to host}    { puts {ERR|UNREACHABLE}  }
    {Network unreachable} { puts {ERR|UNREACHABLE}  }
    {Host key verification failed} { puts {ERR|HOST_KEY} }
    -re {##RESULT\|[^#]+##} {
        puts \$expect_out(0,string)
    }
    timeout { puts {ERR|TIMEOUT} }
    eof {}
}
EXPECT_SCRIPT

    local result
    result=$(SSH_PASS="$SSH_PASS" expect "$expfile" 2>/dev/null)
    rm -f "$expfile"
    printf '%s\n' "$result"
}

# ── 출력 헬퍼 ─────────────────────────────────────────────────────────────────
cpu_color() {       # cpu_color <숫자>  →  색상 포함 "숫자%"
    local v="$1" n
    n=$(printf "%.0f" "$v" 2>/dev/null) || n=0
    if   [[ $n -ge 80 ]]; then printf '%s%s%%%s' "$RED" "$v" "$RST"
    elif [[ $n -ge 50 ]]; then printf '%s%s%%%s' "$YEL" "$v" "$RST"
    else                       printf '%s%s%%%s' "$GRN" "$v" "$RST"
    fi
}

status_color() {    # status_color <상태>  →  색상 포함 상태 문자열
    case "$1" in
        Ready)       printf '%sReady%s'       "$GRN" "$RST" ;;
        Timeout)     printf '%sTimeout%s'     "$YEL" "$RST" ;;
        AuthFail)    printf '%sAuthFail%s'    "$RED" "$RST" ;;
        ConnRefused) printf '%sConnRefused%s' "$RED" "$RST" ;;
        Unreachable) printf '%sUnreachable%s' "$RED" "$RST" ;;
        HostKey)     printf '%sHostKey%s'     "$RED" "$RST" ;;
        *)           printf '%s%s%s'          "$RED" "$1" "$RST" ;;
    esac
}

# 구분선 (112자)
divider() { printf '─%.0s' {1..112}; printf '\n'; }

# ── 메인 ──────────────────────────────────────────────────────────────────────
main() {
    command -v expect &>/dev/null \
        || { printf '%bError:%b expect 없음. yum/apt install expect\n' "$RED" "$RST" >&2; exit 1; }

    local start=$SECONDS

    # 서버 목록 배열로 수집
    local -a srv_ips=() srv_names=()
    while IFS=' ' read -r ip name; do
        srv_ips+=("$ip")
        srv_names+=("$name")
    done < <(parse_hosts)

    local total=${#srv_ips[@]}
    [[ $total -eq 0 ]] && {
        echo "  /etc/hosts 에서 라우팅 가능한 서버를 찾지 못했습니다."
        echo "  예시:  192.168.1.10  my-server"
        exit 0
    }

    printf '\n%b[server-check]%b  user=%s  port=%s  servers=%d\n\n' \
        "${BLD}${CYN}" "$RST" "$SSH_USER" "$SSH_PORT" "$total"

    # ── 모든 서버를 병렬로 체크 ───────────────────────────────────────────────
    local -a pids=() tmpfiles=()
    for (( i=0; i<total; i++ )); do
        local tf
        tf=$(mktemp /tmp/.sc_out_XXXXXX)
        tmpfiles+=("$tf")
        check_server "${srv_ips[$i]}" "${srv_names[$i]}" > "$tf" &
        pids+=($!)
    done

    # ── 헤더 출력 ─────────────────────────────────────────────────────────────
    printf '%b  %-3s  %-24s %-17s %-13s %-9s %s%b\n' \
        "$BLD" "#" "NAME" "IP" "STATUS" "CPU%" "TOP-5 PROCESSES  (cmd:cpu%)" "$RST"
    divider

    local ready=0 failed=0 timed=0

    # ── 결과 순서대로 수집·출력 ───────────────────────────────────────────────
    for (( i=0; i<total; i++ )); do
        wait "${pids[$i]}" 2>/dev/null || true
        local raw
        raw=$(cat "${tmpfiles[$i]}" 2>/dev/null)
        rm -f "${tmpfiles[$i]}"

        local status cpu_val procs_str

        if [[ "$raw" == *"##RESULT|"* ]]; then
            local inner
            inner=$(printf '%s\n' "$raw" \
                    | sed -n 's/.*##RESULT|\([^#]*\)##.*/\1/p' | head -1)
            cpu_val=$(printf '%s\n' "$inner" | cut -d'|' -f1)
            procs_str=$(printf '%s\n' "$inner" | cut -d'|' -f2-)
            status="Ready";       (( ready++ ))
        elif [[ "$raw" == *"AUTH_FAIL"* ]];    then
            status="AuthFail";    cpu_val="-"; procs_str="-"; (( failed++ ))
        elif [[ "$raw" == *"CONN_REFUSED"* ]]; then
            status="ConnRefused"; cpu_val="-"; procs_str="-"; (( failed++ ))
        elif [[ "$raw" == *"UNREACHABLE"* ]];  then
            status="Unreachable"; cpu_val="-"; procs_str="-"; (( failed++ ))
        elif [[ "$raw" == *"HOST_KEY"* ]];     then
            status="HostKey";     cpu_val="-"; procs_str="-"; (( failed++ ))
        elif [[ "$raw" == *"TIMEOUT"* ]];      then
            status="Timeout";     cpu_val="-"; procs_str="-"; (( timed++ ))
        else
            status="Unknown";     cpu_val="-"; procs_str="${raw:0:40}"; (( failed++ ))
        fi

        local c_status c_cpu
        c_status=$(status_color "$status")
        if [[ "$cpu_val" =~ ^[0-9] ]]; then
            c_cpu=$(cpu_color "$cpu_val")
        else
            c_cpu="$cpu_val"
        fi

        # 행 출력 (ANSI 코드가 printf 정렬에 영향 안 주도록 수동 패딩)
        printf '  %-3s  %-24s %-17s ' \
            "$((i+1))." "${srv_names[$i]}" "${srv_ips[$i]}"
        printf '%s%*s' "$c_status" $(( 13 - ${#status} )) ""
        printf '%s%*s' "$c_cpu"    $(( 8  - ${#cpu_val} )) "  "
        printf '%s\n'  "${procs_str:--}"
    done

    divider
    printf '  Servers: %b%d%b  ' "$BLD"  "$total"  "$RST"
    printf 'Ready: %b%b%d%b  '   "$GRN" "$BLD" "$ready"  "$RST"
    printf 'Timeout: %b%b%d%b  ' "$YEL" "$BLD" "$timed"  "$RST"
    printf 'Failed: %b%b%d%b  '  "$RED" "$BLD" "$failed" "$RST"
    printf 'Elapsed: %ds\n\n' $(( SECONDS - start ))
}

main "$@"
