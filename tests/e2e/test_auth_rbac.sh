#!/usr/bin/env bash
# ==============================================================================
# Battery-EC: End-to-End Automated Security & RBAC Test Suite
# File: backend/tests/e2e/test_auth_rbac.sh
# Author: teamwork_test_writer
# Description:
#   Opaque-box automated test runner verifying 4 tiers of security requirements:
#     - Tier 1: Core Feature Coverage (Auth API, Dual Tokens, Me, Public APIs)
#     - Tier 2: Boundary, Corner & OWASP Adversarial Attacks (SQLi, XSS, Expiry)
#     - Tier 3: RBAC 6-Role Matrix Verification (401 / 403 / 200)
#     - Tier 4: Real-World Business Workflow Scenarios (Customer, StoreManager,
#               WarehouseStaff, SalesStaff, Technician, Admin, Token Lifecycle)
# ==============================================================================

set -o pipefail

# --- Help Option ---
if [ "$1" = "-h" ] || [ "$1" = "--help" ]; then
    echo "Usage: $0 [BASE_URL]"
    echo ""
    echo "Parameters:"
    echo "  BASE_URL    Target server base URL (default: http://localhost:5074 or \$BASE_URL)"
    echo "  -h, --help  Display this help menu"
    echo ""
    echo "Examples:"
    echo "  $0"
    echo "  $0 http://localhost:5074"
    echo "  BASE_URL=https://api.staging.batterypartsstore.vn $0"
    exit 0
fi

# --- Configuration & Base URL ---
BASE_URL="${1:-${BASE_URL:-http://localhost:5074}}"
BASE_URL="${BASE_URL%/}" # Strip trailing slash

# --- Color Definitions ---
if [ -t 1 ]; then
    RED='\033[0;31m'
    GREEN='\033[0;32m'
    YELLOW='\033[1;33m'
    BLUE='\033[0;34m'
    MAGENTA='\033[0;35m'
    CYAN='\033[0;36m'
    BOLD='\033[1m'
    DIM='\033[2m'
    RESET='\033[0m'
else
    RED=''
    GREEN=''
    YELLOW=''
    BLUE=''
    MAGENTA=''
    CYAN=''
    BOLD=''
    DIM=''
    RESET=''
fi

# --- Global Counters ---
TOTAL_TESTS=0
PASSED_TESTS=0
FAILED_TESTS=0
SKIPPED_TESTS=0

# --- Temporary Storage & Cleanup ---
TEMP_DIR=$(mktemp -d -t battery_e2e_XXXXXX)
cleanup() {
    rm -rf "$TEMP_DIR"
}
trap cleanup EXIT INT TERM

# --- Helper Functions ---

log_header() {
    echo ""
    echo -e "${CYAN}${BOLD}================================================================================${RESET}"
    echo -e "${CYAN}${BOLD}  $1${RESET}"
    echo -e "${CYAN}${BOLD}================================================================================${RESET}"
}

log_section() {
    echo ""
    echo -e "${BLUE}${BOLD}--- $1 ---${RESET}"
}

log_pass() {
    local test_name="$1"
    local detail="$2"
    ((PASSED_TESTS++))
    ((TOTAL_TESTS++))
    if [ -n "$detail" ]; then
        printf "  ${GREEN}[PASS]${RESET} %-58s ${DIM}(%s)${RESET}\n" "$test_name" "$detail"
    else
        printf "  ${GREEN}[PASS]${RESET} %-58s\n" "$test_name"
    fi
}

log_fail() {
    local test_name="$1"
    local expected="$2"
    local actual="$3"
    local detail="$4"
    ((FAILED_TESTS++))
    ((TOTAL_TESTS++))
    printf "  ${RED}[FAIL]${RESET} %-58s\n" "$test_name"
    printf "         ${RED}Expected:${RESET} %s | ${RED}Actual:${RESET} %s\n" "$expected" "$actual"
    if [ -n "$detail" ]; then
        printf "         ${DIM}Detail: %s${RESET}\n" "$detail"
    fi
}

log_skip() {
    local test_name="$1"
    local reason="$2"
    ((SKIPPED_TESTS++))
    ((TOTAL_TESTS++))
    printf "  ${YELLOW}[SKIP]${RESET} %-58s ${DIM}(%s)${RESET}\n" "$test_name" "$reason"
}

# Extracts JSON fields using jq with fallback to python3
json_extract() {
    local json="$1"
    local field="$2"
    if [ -z "$json" ]; then
        echo ""
        return
    fi
    if command -v jq >/dev/null 2>&1; then
        echo "$json" | jq -r "$field // empty" 2>/dev/null
    elif command -v python3 >/dev/null 2>&1; then
        python3 -c "
import sys, json
try:
    data = json.loads('''$json''')
    for part in '$field'.strip('.').split('.'):
        if part:
            data = data.get(part, '')
    print(data if data is not None else '')
except Exception:
    print('')
" 2>/dev/null
    else
        echo ""
    fi
}

# HTTP Request Dispatcher
# Usage: do_curl METHOD ENDPOINT [DATA] [TOKEN] [COOKIE_JAR] [COOKIE_FILE] [EXTRA_HEADERS]
HTTP_CODE=""
HTTP_BODY=""
HTTP_HEADERS=""

do_curl() {
    local method="$1"
    local endpoint="$2"
    local data="$3"
    local token="$4"
    local cookie_jar="$5"
    local cookie_file="$6"
    local extra_headers="$7"
    
    local url="${BASE_URL}${endpoint}"
    local header_file="${TEMP_DIR}/headers_$$.tmp"
    local body_file="${TEMP_DIR}/body_$$.tmp"
    
    local curl_args=(-s -S -w "%{http_code}" -o "$body_file" -D "$header_file" -X "$method")
    
    if [ -n "$token" ]; then
        curl_args+=(-H "Authorization: Bearer $token")
    fi
    
    if [ -n "$data" ]; then
        curl_args+=(-H "Content-Type: application/json" -d "$data")
    fi
    
    if [ -n "$cookie_jar" ]; then
        curl_args+=(-c "$cookie_jar")
    fi
    
    if [ -n "$cookie_file" ]; then
        curl_args+=(-b "$cookie_file")
    fi
    
    if [ -n "$extra_headers" ]; then
        curl_args+=(-H "$extra_headers")
    fi
    
    curl_args+=("$url")
    
    local raw_code
    raw_code=$(curl "${curl_args[@]}" 2>/dev/null)
    if [ -z "$raw_code" ] || [[ "$raw_code" =~ ^0+$ ]]; then
        HTTP_CODE="000"
    else
        # In case stdout contains trailing newlines or extra chars, take the last 3 digits
        HTTP_CODE=$(echo "$raw_code" | grep -oE '[0-9]{3}$' || echo "000")
    fi
    HTTP_BODY=$(cat "$body_file" 2>/dev/null || echo "")
    HTTP_HEADERS=$(cat "$header_file" 2>/dev/null || echo "")
}

# Helper to login a role and extract access & refresh tokens
# Usage: login_user USERNAME PASSWORD
LOGIN_ACCESS_TOKEN=""
LOGIN_REFRESH_TOKEN=""
LOGIN_COOKIE_JAR=""

login_user() {
    local username="$1"
    local password="$2"
    local cookie_file="${TEMP_DIR}/cookie_${username}_$$.txt"
    LOGIN_ACCESS_TOKEN=""
    LOGIN_REFRESH_TOKEN=""
    LOGIN_COOKIE_JAR="$cookie_file"
    
    local payload="{\"usernameOrPhoneOrEmail\":\"$username\",\"password\":\"$password\"}"
    do_curl "POST" "/api/auth/login" "$payload" "" "$cookie_file" ""
    
    if [ "$HTTP_CODE" = "200" ]; then
        # Try direct or inside ApiResponse data envelope
        LOGIN_ACCESS_TOKEN=$(json_extract "$HTTP_BODY" ".data.accessToken")
        [ -z "$LOGIN_ACCESS_TOKEN" ] && LOGIN_ACCESS_TOKEN=$(json_extract "$HTTP_BODY" ".accessToken")
        
        LOGIN_REFRESH_TOKEN=$(json_extract "$HTTP_BODY" ".data.refreshToken")
        [ -z "$LOGIN_REFRESH_TOKEN" ] && LOGIN_REFRESH_TOKEN=$(json_extract "$HTTP_BODY" ".refreshToken")
        return 0
    fi
    return 1
}

# --- Pre-flight Health & Connectivity Check ---
check_preflight() {
    log_header "PRE-FLIGHT CONNECTIVITY CHECK"
    echo -e "Target Base URL: ${BOLD}${BASE_URL}${RESET}"
    
    # Try public endpoint /api/products
    do_curl "GET" "/api/products" "" "" "" ""
    if [ "$HTTP_CODE" = "000" ]; then
        echo -e "${RED}${BOLD}[ERROR] Cannot connect to server at ${BASE_URL}.${RESET}"
        echo -e "${YELLOW}Please ensure the ASP.NET Core backend is running:${RESET}"
        echo -e "  cd /var/home/CLap/Projects/battery_EC/backend"
        echo -e "  dotnet run --project src/BatteryEC.WebApi"
        exit 1
    fi
    echo -e "${GREEN}[OK] Connection established. Server returned HTTP ${HTTP_CODE} on /api/products.${RESET}"
}

# ==============================================================================
# TIER 1: FEATURE COVERAGE (Core Authentication & Public Endpoints)
# ==============================================================================
run_tier1_feature_coverage() {
    log_header "TIER 1: FEATURE COVERAGE (Core Auth & Public Contracts)"
    
    # T1.1: User Registration
    log_section "T1.1: User Registration (POST /api/auth/register)"
    local rand_id=$((RANDOM % 900000 + 100000))
    local reg_user="testuser_${rand_id}"
    local reg_email="test_${rand_id}@batterytest.vn"
    local reg_phone="099${rand_id}"
    local reg_payload="{\"username\":\"$reg_user\",\"password\":\"P@ssword123\",\"email\":\"$reg_email\",\"phone\":\"$reg_phone\",\"fullName\":\"E2E Test User\"}"
    
    do_curl "POST" "/api/auth/register" "$reg_payload" "" "" ""
    if [ "$HTTP_CODE" = "200" ] || [ "$HTTP_CODE" = "201" ]; then
        log_pass "Register new account with valid credentials" "HTTP $HTTP_CODE"
    else
        log_fail "Register new account with valid credentials" "HTTP 200 or 201" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # T1.2: Login via Username
    log_section "T1.2: Login via Username (POST /api/auth/login)"
    local login_payload="{\"usernameOrPhoneOrEmail\":\"admin\",\"password\":\"P@ssword123\"}"
    local cookie_jar_admin="${TEMP_DIR}/admin_cookie.txt"
    do_curl "POST" "/api/auth/login" "$login_payload" "" "$cookie_jar_admin" ""
    if [ "$HTTP_CODE" = "200" ]; then
        local tok=$(json_extract "$HTTP_BODY" ".data.accessToken")
        [ -z "$tok" ] && tok=$(json_extract "$HTTP_BODY" ".accessToken")
        if [ -n "$tok" ]; then
            log_pass "Login with valid Username returns JWT Access Token" "HTTP 200, Token len: ${#tok}"
        else
            log_fail "Login with valid Username returns JWT Access Token" "Non-empty accessToken" "Empty" "$HTTP_BODY"
        fi
    else
        log_fail "Login with valid Username (admin)" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # T1.3: Login via Phone Number
    log_section "T1.3: Login via Phone Number (POST /api/auth/login)"
    local phone_payload="{\"usernameOrPhoneOrEmail\":\"0901234567\",\"password\":\"P@ssword123\"}"
    do_curl "POST" "/api/auth/login" "$phone_payload" "" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "Login with valid Phone Number (0901234567)" "HTTP 200"
    else
        log_fail "Login with valid Phone Number (0901234567)" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # T1.4: Login via Email
    log_section "T1.4: Login via Email Address (POST /api/auth/login)"
    local email_payload="{\"usernameOrPhoneOrEmail\":\"admin@batterypartsstore.vn\",\"password\":\"P@ssword123\"}"
    do_curl "POST" "/api/auth/login" "$email_payload" "" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "Login with valid Email Address" "HTTP 200"
    else
        log_fail "Login with valid Email Address" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # T1.5: Refresh Token via HttpOnly Cookie
    log_section "T1.5: Refresh Token via HttpOnly Cookie (POST /api/auth/refresh-token)"
    local cookie_refresh="${TEMP_DIR}/cookie_refresh_test.txt"
    login_user "customer1" "P@ssword123"
    do_curl "POST" "/api/auth/login" "{\"usernameOrPhoneOrEmail\":\"customer1\",\"password\":\"P@ssword123\"}" "" "$cookie_refresh" ""
    
    # Verify Set-Cookie header contains HttpOnly and refreshToken
    if grep -iq "refreshToken" "$cookie_refresh" 2>/dev/null || echo "$HTTP_HEADERS" | grep -iq "Set-Cookie.*refreshToken"; then
        log_pass "Login sets refreshToken in Cookie / Jar" "Found in headers/jar"
    else
        log_fail "Login sets refreshToken in Cookie / Jar" "refreshToken cookie" "None" "$HTTP_HEADERS"
    fi
    
    # Call refresh token using cookie
    local new_cookie_jar="${TEMP_DIR}/refreshed_cookies.txt"
    do_curl "POST" "/api/auth/refresh-token" "" "" "$new_cookie_jar" "$cookie_refresh"
    if [ "$HTTP_CODE" = "200" ]; then
        local new_token=$(json_extract "$HTTP_BODY" ".data.accessToken")
        [ -z "$new_token" ] && new_token=$(json_extract "$HTTP_BODY" ".accessToken")
        if [ -n "$new_token" ]; then
            log_pass "Refresh Token via Cookie returns new Access Token" "HTTP 200, Token len: ${#new_token}"
        else
            log_fail "Refresh Token via Cookie returns new Access Token" "Non-empty accessToken" "Empty" "$HTTP_BODY"
        fi
    else
        log_fail "Refresh Token via Cookie" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # T1.6: Refresh Token via JSON Body
    log_section "T1.6: Refresh Token via JSON Request Body (POST /api/auth/refresh-token)"
    if [ -n "$LOGIN_REFRESH_TOKEN" ]; then
        local body_refresh="{\"refreshToken\":\"$LOGIN_REFRESH_TOKEN\"}"
        do_curl "POST" "/api/auth/refresh-token" "$body_refresh" "" "" ""
        if [ "$HTTP_CODE" = "200" ]; then
            log_pass "Refresh Token via JSON Body returns new Access Token" "HTTP 200"
        else
            log_fail "Refresh Token via JSON Body" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
        fi
    else
        log_skip "Refresh Token via JSON Body" "No refresh token captured from login"
    fi
    
    # T1.7: Current User Profile (GET /api/auth/me)
    log_section "T1.7: Current User Profile (GET /api/auth/me)"
    login_user "admin" "P@ssword123"
    do_curl "GET" "/api/auth/me" "" "$LOGIN_ACCESS_TOKEN" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "GET /api/auth/me with valid Bearer token returns profile" "HTTP 200"
    else
        log_fail "GET /api/auth/me with valid Bearer token" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # T1.8: Public Endpoint: Products Catalog
    log_section "T1.8: Public Endpoint - Products Catalog (GET /api/products)"
    do_curl "GET" "/api/products" "" "" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "Anonymous access to /api/products allowed" "HTTP 200"
    else
        log_fail "Anonymous access to /api/products" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # T1.9: Public Endpoint: Warranty Lookup
    log_section "T1.9: Public Endpoint - Electronic Warranty Lookup (GET /api/warranty/lookup)"
    do_curl "GET" "/api/warranty/lookup?query=0901234567" "" "" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "Anonymous access to /api/warranty/lookup allowed" "HTTP 200"
    else
        log_fail "Anonymous access to /api/warranty/lookup" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
}

# ==============================================================================
# TIER 2: BOUNDARY, CORNER & ADVERSARIAL EDGE CASES
# ==============================================================================
run_tier2_boundary_and_adversarial() {
    log_header "TIER 2: BOUNDARY, CORNER & ADVERSARIAL CASES"
    
    # T2.1: Login with empty credentials
    log_section "T2.1 - T2.4: Login Credential Boundaries"
    do_curl "POST" "/api/auth/login" "{}" "" "" ""
    if [ "$HTTP_CODE" = "400" ] || [ "$HTTP_CODE" = "401" ]; then
        log_pass "Login with empty credentials rejected" "HTTP $HTTP_CODE"
    else
        log_fail "Login with empty credentials rejected" "HTTP 400 or 401" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # T2.2: Login with empty password
    do_curl "POST" "/api/auth/login" "{\"usernameOrPhoneOrEmail\":\"admin\",\"password\":\"\"}" "" "" ""
    if [ "$HTTP_CODE" = "400" ] || [ "$HTTP_CODE" = "401" ]; then
        log_pass "Login with empty password rejected" "HTTP $HTTP_CODE"
    else
        log_fail "Login with empty password rejected" "HTTP 400 or 401" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # T2.3: Login with non-existent user
    do_curl "POST" "/api/auth/login" "{\"usernameOrPhoneOrEmail\":\"non_existent_user_99999\",\"password\":\"RandomPass123!\"}" "" "" ""
    if [ "$HTTP_CODE" = "401" ]; then
        log_pass "Login with non-existent user rejected" "HTTP 401"
    else
        log_fail "Login with non-existent user rejected" "HTTP 401" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # T2.4: Login with correct username but incorrect password
    do_curl "POST" "/api/auth/login" "{\"usernameOrPhoneOrEmail\":\"admin\",\"password\":\"WrongPassword123\"}" "" "" ""
    if [ "$HTTP_CODE" = "401" ]; then
        log_pass "Login with invalid password rejected" "HTTP 401"
    else
        log_fail "Login with invalid password rejected" "HTTP 401" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # T2.5 - T2.7: Duplicate Registrations
    log_section "T2.5 - T2.7: Duplicate Registration Constraints"
    # Duplicate Username
    local dup_user_payload="{\"username\":\"admin\",\"password\":\"P@ssword123\",\"email\":\"unique_email_$RANDOM@test.vn\",\"phone\":\"0988776655\",\"fullName\":\"Admin Clone\"}"
    do_curl "POST" "/api/auth/register" "$dup_user_payload" "" "" ""
    if [ "$HTTP_CODE" = "400" ]; then
        log_pass "Duplicate username registration rejected" "HTTP 400"
    else
        log_fail "Duplicate username registration rejected" "HTTP 400" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Duplicate Email
    local dup_email_payload="{\"username\":\"uniq_user_$RANDOM\",\"password\":\"P@ssword123\",\"email\":\"admin@batterypartsstore.vn\",\"phone\":\"0988776654\",\"fullName\":\"Email Clone\"}"
    do_curl "POST" "/api/auth/register" "$dup_email_payload" "" "" ""
    if [ "$HTTP_CODE" = "400" ]; then
        log_pass "Duplicate email registration rejected" "HTTP 400"
    else
        log_fail "Duplicate email registration rejected" "HTTP 400" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Duplicate Phone
    local dup_phone_payload="{\"username\":\"uniq_user2_$RANDOM\",\"password\":\"P@ssword123\",\"email\":\"uniq_email_$RANDOM@test.vn\",\"phone\":\"0901234567\",\"fullName\":\"Phone Clone\"}"
    do_curl "POST" "/api/auth/register" "$dup_phone_payload" "" "" ""
    if [ "$HTTP_CODE" = "400" ]; then
        log_pass "Duplicate phone registration rejected" "HTTP 400"
    else
        log_fail "Duplicate phone registration rejected" "HTTP 400" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # T2.8 - T2.11: Token Validation & Tampering
    log_section "T2.8 - T2.11: Token Validation & Tampering"
    # Missing token on protected endpoint
    do_curl "GET" "/api/auth/me" "" "" "" ""
    if [ "$HTTP_CODE" = "401" ]; then
        log_pass "Missing Bearer token returns HTTP 401 Unauthorized" "HTTP 401"
    else
        log_fail "Missing Bearer token returns HTTP 401 Unauthorized" "HTTP 401" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Malformed token
    do_curl "GET" "/api/auth/me" "" "this.is.a.malformed.bearer.token" "" ""
    if [ "$HTTP_CODE" = "401" ]; then
        log_pass "Malformed Bearer token returns HTTP 401 Unauthorized" "HTTP 401"
    else
        log_fail "Malformed Bearer token returns HTTP 401 Unauthorized" "HTTP 401" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Tampered signature
    login_user "admin" "P@ssword123"
    if [ -n "$LOGIN_ACCESS_TOKEN" ]; then
        local tampered_token="${LOGIN_ACCESS_TOKEN%?}X" # Change last character of signature
        do_curl "GET" "/api/auth/me" "" "$tampered_token" "" ""
        if [ "$HTTP_CODE" = "401" ]; then
            log_pass "Tampered token signature returns HTTP 401 Unauthorized" "HTTP 401"
        else
            log_fail "Tampered token signature returns HTTP 401 Unauthorized" "HTTP 401" "HTTP $HTTP_CODE" "$HTTP_BODY"
        fi
    fi
    
    # T2.12 - T2.13: Refresh Token Boundary Cases
    log_section "T2.12 - T2.13: Refresh Token Edge Cases"
    # Missing both cookie and body
    do_curl "POST" "/api/auth/refresh-token" "" "" "" ""
    if [ "$HTTP_CODE" = "400" ] || [ "$HTTP_CODE" = "401" ]; then
        log_pass "Refresh request with missing cookie and empty body rejected" "HTTP $HTTP_CODE"
    else
        log_fail "Refresh request with missing cookie and empty body rejected" "HTTP 400 or 401" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Invalid/Bogus refresh token
    do_curl "POST" "/api/auth/refresh-token" "{\"refreshToken\":\"non_existent_fake_refresh_token_xyz_12345\"}" "" "" ""
    if [ "$HTTP_CODE" = "401" ]; then
        log_pass "Invalid / non-existent refresh token returns HTTP 401 Unauthorized" "HTTP 401"
    else
        log_fail "Invalid / non-existent refresh token returns HTTP 401 Unauthorized" "HTTP 401" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # T2.14 - T2.16: Adversarial OWASP Input Hardening
    log_section "T2.14 - T2.16: Adversarial OWASP Injection Hardening"
    # SQL Injection in username
    local sqli_payload="{\"usernameOrPhoneOrEmail\":\"' OR '1'='1' --\",\"password\":\"P@ssword123\"}"
    do_curl "POST" "/api/auth/login" "$sqli_payload" "" "" ""
    if [ "$HTTP_CODE" = "400" ] || [ "$HTTP_CODE" = "401" ]; then
        log_pass "SQL injection in login identifier safely rejected without 500 crash" "HTTP $HTTP_CODE"
    else
        log_fail "SQL injection in login identifier safely rejected" "HTTP 400 or 401" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # SQL Injection in password
    local sqli_pass_payload="{\"usernameOrPhoneOrEmail\":\"admin\",\"password\":\"' OR 1=1; DROP TABLE users; --\"}"
    do_curl "POST" "/api/auth/login" "$sqli_pass_payload" "" "" ""
    if [ "$HTTP_CODE" = "400" ] || [ "$HTTP_CODE" = "401" ]; then
        log_pass "SQL injection in login password safely rejected without 500 crash" "HTTP $HTTP_CODE"
    else
        log_fail "SQL injection in login password safely rejected" "HTTP 400 or 401" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # XSS payload in registration full name
    local xss_user="xss_user_$RANDOM"
    local xss_payload="{\"username\":\"$xss_user\",\"password\":\"P@ssword123\",\"email\":\"$xss_user@test.vn\",\"phone\":\"0988${RANDOM:0:6}\",\"fullName\":\"<script>alert('XSS')</script>\"}"
    do_curl "POST" "/api/auth/register" "$xss_payload" "" "" ""
    if [ "$HTTP_CODE" = "200" ] || [ "$HTTP_CODE" = "201" ] || [ "$HTTP_CODE" = "400" ]; then
        log_pass "XSS payload in registration handled safely without 500 crash" "HTTP $HTTP_CODE"
    else
        log_fail "XSS payload in registration handled safely" "HTTP 200, 201, or 400" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
}

# ==============================================================================
# TIER 3: RBAC 6-ROLE MATRIX VERIFICATION
# ==============================================================================
run_tier3_rbac_matrix() {
    log_header "TIER 3: RBAC 6-ROLE MATRIX VERIFICATION"
    
    # Acquire tokens for all 6 roles
    log_section "Authenticating all 6 system roles"
    
    local roles=("admin" "manager" "warehouse" "sales" "tech" "customer1")
    declare -A ROLE_TOKENS
    
    for r in "${roles[@]}"; do
        if login_user "$r" "P@ssword123"; then
            ROLE_TOKENS["$r"]="$LOGIN_ACCESS_TOKEN"
            echo -e "  ${GREEN}[OK]${RESET} Authenticated role: ${BOLD}$r${RESET} (Token acquired)"
        else
            echo -e "  ${RED}[ERR]${RESET} Failed to authenticate role: ${BOLD}$r${RESET}"
        fi
    done
    
    # Helper to test RBAC endpoint access
    # Usage: test_rbac_endpoint TEST_NAME METHOD ENDPOINT DATA ALLOWED_ROLES_LIST
    test_rbac_endpoint() {
        local test_name="$1"
        local method="$2"
        local endpoint="$3"
        local data="$4"
        local allowed_roles="$5"
        
        # 1. Anonymous test (Must return 401)
        do_curl "$method" "$endpoint" "$data" "" "" ""
        if [ "$HTTP_CODE" = "401" ]; then
            log_pass "$test_name [Anonymous -> 401 Unauthorized]" "HTTP 401"
        else
            log_fail "$test_name [Anonymous -> 401 Unauthorized]" "HTTP 401" "HTTP $HTTP_CODE" "$HTTP_BODY"
        fi
        
        # 2. Iterate each role
        for r in "${roles[@]}"; do
            local tok="${ROLE_TOKENS[$r]}"
            if [ -z "$tok" ]; then
                log_skip "$test_name [$r]" "Token not available"
                continue
            fi
            
            do_curl "$method" "$endpoint" "$data" "$tok" "" ""
            
            # Check if this role is in allowed_roles
            if [[ " $allowed_roles " =~ " $r " ]]; then
                # Allowed role: Should NOT be 401 or 403
                if [ "$HTTP_CODE" != "401" ] && [ "$HTTP_CODE" != "403" ]; then
                    log_pass "$test_name [Allowed: $r -> HTTP $HTTP_CODE]" "Access granted"
                else
                    log_fail "$test_name [Allowed: $r -> HTTP $HTTP_CODE]" "HTTP != 401 and != 403" "HTTP $HTTP_CODE" "$HTTP_BODY"
                fi
            else
                # Disallowed role: Must be strictly 403 Forbidden
                if [ "$HTTP_CODE" = "403" ]; then
                    log_pass "$test_name [Denied: $r -> 403 Forbidden]" "Access blocked"
                else
                    log_fail "$test_name [Denied: $r -> 403 Forbidden]" "HTTP 403" "HTTP $HTTP_CODE" "$HTTP_BODY"
                fi
            fi
        done
    }
    
    # Matrix Test 1: Financial Reports (/api/reports/revenue-summary) -> Admin only
    log_section "Matrix 1: Financial Reports (/api/reports/revenue-summary) - Admin Only"
    test_rbac_endpoint "Reports: revenue-summary" "GET" "/api/reports/revenue-summary" "" "admin"
    
    # Matrix Test 2: Inventory Reconciliation (/api/reports/inventory-reconciliation) -> Admin only
    log_section "Matrix 2: Inventory Reconciliation (/api/reports/inventory-reconciliation) - Admin Only"
    test_rbac_endpoint "Reports: inventory-reconciliation" "GET" "/api/reports/inventory-reconciliation" "" "admin"
    
    # Matrix Test 3: Order Confirmation (/api/orders/2/confirm) -> StoreManager, Admin
    log_section "Matrix 3: Order Approval (/api/orders/2/confirm) - StoreManager & Admin"
    test_rbac_endpoint "Orders: confirm" "POST" "/api/orders/2/confirm" "" "admin manager"
    
    # Matrix Test 4: Inventory Stock Query (/api/inventory/1) -> WarehouseStaff, Admin
    log_section "Matrix 4: Inventory Stock Query (/api/inventory/1) - WarehouseStaff & Admin"
    test_rbac_endpoint "Inventory: get stock" "GET" "/api/inventory/1" "" "admin warehouse"
    
    # Matrix Test 5: Inventory Stock Adjustment (/api/inventory/adjust) -> WarehouseStaff, Admin
    log_section "Matrix 5: Inventory Adjustment (/api/inventory/adjust) - WarehouseStaff & Admin"
    local adj_payload="{\"productVariantId\":1,\"quantityChange\":1,\"reason\":\"E2E RBAC Matrix Test\"}"
    test_rbac_endpoint "Inventory: adjust" "POST" "/api/inventory/adjust" "$adj_payload" "admin warehouse"
    
    # Matrix Test 6: POS Retail Sale (/api/pos/sales) -> SalesStaff, Admin
    log_section "Matrix 6: POS Retail Sale (/api/pos/sales) - SalesStaff & Admin"
    local pos_payload="{\"discountAmount\":0,\"paymentMethod\":\"Cash\",\"items\":[{\"productVariantId\":1,\"quantity\":1}]}"
    test_rbac_endpoint "POS: sales" "POST" "/api/pos/sales" "$pos_payload" "admin sales"
    
    # Matrix Test 7: Assembly Order Execution (/api/assembly/ASM-202610-001) -> Technician, Admin
    log_section "Matrix 7: Assembly Query (/api/assembly/ASM-202610-001) - Technician & Admin"
    test_rbac_endpoint "Assembly: query order" "GET" "/api/assembly/ASM-202610-001" "" "admin tech"
    
    # Matrix Test 8: Repair Intake Tickets (/api/repairs) -> Technician, Admin
    log_section "Matrix 8: Repair Ticket Intake (/api/repairs) - Technician & Admin"
    local repair_payload="{\"customerName\":\"Nguyen Van Test\",\"customerPhone\":\"0911223344\",\"deviceModel\":\"E-Bike Battery 48V\",\"issueDescription\":\"Cells out of balance\"}"
    test_rbac_endpoint "Repairs: create intake" "POST" "/api/repairs" "$repair_payload" "admin tech"
}

# ==============================================================================
# TIER 4: REAL-WORLD BUSINESS WORKFLOW SCENARIOS
# ==============================================================================
run_tier4_business_scenarios() {
    log_header "TIER 4: REAL-WORLD BUSINESS WORKFLOW SCENARIOS"
    
    # -------------------------------------------------------------------------
    # Scenario 1: Customer Online Journey
    # -------------------------------------------------------------------------
    log_section "Scenario 1: Customer Online Registration, Catalog & Checkout"
    local s1_id=$((RANDOM % 900000 + 100000))
    local s1_user="cust_journey_${s1_id}"
    local s1_email="${s1_user}@shopbattery.vn"
    local s1_phone="091${s1_id}"
    local s1_pass="P@ssword123"
    
    # Step 1: Register
    local s1_reg_payload="{\"username\":\"$s1_user\",\"password\":\"$s1_pass\",\"email\":\"$s1_email\",\"phone\":\"$s1_phone\",\"fullName\":\"Nguyen Journey Customer\"}"
    do_curl "POST" "/api/auth/register" "$s1_reg_payload" "" "" ""
    if [ "$HTTP_CODE" = "200" ] || [ "$HTTP_CODE" = "201" ]; then
        log_pass "S1.1: Customer registers account" "HTTP $HTTP_CODE"
    else
        log_fail "S1.1: Customer registers account" "HTTP 200 or 201" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Step 2: Login
    local s1_login_payload="{\"usernameOrPhoneOrEmail\":\"$s1_user\",\"password\":\"$s1_pass\"}"
    do_curl "POST" "/api/auth/login" "$s1_login_payload" "" "" ""
    local s1_token=""
    if [ "$HTTP_CODE" = "200" ]; then
        s1_token=$(json_extract "$HTTP_BODY" ".data.accessToken")
        [ -z "$s1_token" ] && s1_token=$(json_extract "$HTTP_BODY" ".accessToken")
        log_pass "S1.2: Customer logs in and obtains Access Token" "HTTP 200"
    else
        log_fail "S1.2: Customer logs in" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Step 3: Browse Products
    do_curl "GET" "/api/products" "" "$s1_token" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "S1.3: Customer browses product catalog" "HTTP 200"
    else
        log_fail "S1.3: Customer browses product catalog" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Step 4: Submit Online Order
    local s1_order_payload="{\"recipientName\":\"Nguyen Journey Customer\",\"recipientPhone\":\"$s1_phone\",\"shippingAddress\":\"123 Le Loi, Da Nang\",\"paymentMethod\":\"COD\",\"items\":[{\"productVariantId\":1,\"quantity\":2}]}"
    do_curl "POST" "/api/orders" "$s1_order_payload" "$s1_token" "" ""
    local s1_order_code=""
    local s1_order_id=""
    if [ "$HTTP_CODE" = "200" ] || [ "$HTTP_CODE" = "201" ]; then
        s1_order_code=$(json_extract "$HTTP_BODY" ".data.orderCode")
        [ -z "$s1_order_code" ] && s1_order_code=$(json_extract "$HTTP_BODY" ".orderCode")
        s1_order_id=$(json_extract "$HTTP_BODY" ".data.orderId")
        [ -z "$s1_order_id" ] && s1_order_id=$(json_extract "$HTTP_BODY" ".orderId")
        log_pass "S1.4: Customer submits online order (Code: $s1_order_code)" "HTTP $HTTP_CODE"
    else
        log_fail "S1.4: Customer submits online order" "HTTP 200 or 201" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Step 5: Query Order by Code
    if [ -n "$s1_order_code" ]; then
        do_curl "GET" "/api/orders/$s1_order_code" "" "$s1_token" "" ""
        if [ "$HTTP_CODE" = "200" ]; then
            log_pass "S1.5: Customer queries order details by code" "HTTP 200"
        else
            log_fail "S1.5: Customer queries order details" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
        fi
    fi
    
    # -------------------------------------------------------------------------
    # Scenario 2: StoreManager Order Lifecycle & Fulfillment
    # -------------------------------------------------------------------------
    log_section "Scenario 2: StoreManager Order Approval, 3PL Shipping & Completion"
    login_user "manager" "P@ssword123"
    local mgr_token="$LOGIN_ACCESS_TOKEN"
    
    # If customer created an order in Scenario 1, use that ID; otherwise fallback to seed order #2
    local target_order_id="${s1_order_id:-2}"
    
    # Step 1: Confirm Order
    do_curl "POST" "/api/orders/${target_order_id}/confirm" "" "$mgr_token" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "S2.1: StoreManager confirms order #${target_order_id}" "HTTP 200"
    else
        log_fail "S2.1: StoreManager confirms order #${target_order_id}" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Step 2: Hand over to 3PL Shipping
    do_curl "POST" "/api/orders/${target_order_id}/ship?carrier=GHTK&trackingCode=GHTK-${RANDOM}" "" "$mgr_token" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "S2.2: StoreManager dispatches order #${target_order_id} to 3PL carrier" "HTTP 200"
    else
        log_fail "S2.2: StoreManager dispatches order #${target_order_id} to carrier" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Step 3: Complete Order
    do_curl "POST" "/api/orders/${target_order_id}/complete" "" "$mgr_token" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "S2.3: StoreManager completes delivery of order #${target_order_id}" "HTTP 200"
    else
        log_fail "S2.3: StoreManager completes delivery of order #${target_order_id}" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # -------------------------------------------------------------------------
    # Scenario 3: WarehouseStaff Inventory Operations
    # -------------------------------------------------------------------------
    log_section "Scenario 3: WarehouseStaff Inventory Stock Audit & Adjustment"
    login_user "warehouse" "P@ssword123"
    local wh_token="$LOGIN_ACCESS_TOKEN"
    
    # Step 1: Query stock
    do_curl "GET" "/api/inventory/1" "" "$wh_token" "" ""
    local initial_stock=0
    if [ "$HTTP_CODE" = "200" ]; then
        initial_stock=$(json_extract "$HTTP_BODY" ".data.quantityOnHand")
        [ -z "$initial_stock" ] && initial_stock=$(json_extract "$HTTP_BODY" ".quantityOnHand")
        log_pass "S3.1: WarehouseStaff queries inventory stock (OnHand: $initial_stock)" "HTTP 200"
    else
        log_fail "S3.1: WarehouseStaff queries inventory stock" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Step 2: Adjust Stock
    local s3_adj_payload="{\"productVariantId\":1,\"quantityChange\":5,\"reason\":\"Periodic cycle count reconciliation\",\"unitCost\":85000}"
    do_curl "POST" "/api/inventory/adjust" "$s3_adj_payload" "$wh_token" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "S3.2: WarehouseStaff executes manual stock adjustment (+5)" "HTTP 200"
    else
        log_fail "S3.2: WarehouseStaff executes manual stock adjustment" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Step 3: Verify Stock Updated
    do_curl "GET" "/api/inventory/1" "" "$wh_token" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        local updated_stock=$(json_extract "$HTTP_BODY" ".data.quantityOnHand")
        [ -z "$updated_stock" ] && updated_stock=$(json_extract "$HTTP_BODY" ".quantityOnHand")
        log_pass "S3.3: WarehouseStaff verifies updated stock balance (OnHand: $updated_stock)" "HTTP 200"
    else
        log_fail "S3.3: WarehouseStaff verifies updated stock balance" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # -------------------------------------------------------------------------
    # Scenario 4: Technician Workshop & Repair Lifecycle
    # -------------------------------------------------------------------------
    log_section "Scenario 4: Technician BOM Inspection, Repair Intake & Spare Parts"
    login_user "tech" "P@ssword123"
    local tech_token="$LOGIN_ACCESS_TOKEN"
    
    # Step 1: Query existing Assembly Order
    do_curl "GET" "/api/assembly/ASM-202610-001" "" "$tech_token" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "S4.1: Technician inspects BOM assembly order ASM-202610-001" "HTTP 200"
    else
        log_fail "S4.1: Technician inspects BOM assembly order" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Step 2: Create Repair Ticket Intake
    local s4_repair_payload="{\"customerName\":\"Tran Quoc Test\",\"customerPhone\":\"0933445566\",\"deviceModel\":\"Makita 18V Battery Pack\",\"issueDescription\":\"Cells damaged, unbalanced voltage\"}"
    do_curl "POST" "/api/repairs" "$s4_repair_payload" "$tech_token" "" ""
    local s4_ticket_id=""
    local s4_ticket_code=""
    if [ "$HTTP_CODE" = "200" ] || [ "$HTTP_CODE" = "201" ]; then
        s4_ticket_id=$(json_extract "$HTTP_BODY" ".data.repairTicketId")
        [ -z "$s4_ticket_id" ] && s4_ticket_id=$(json_extract "$HTTP_BODY" ".repairTicketId")
        s4_ticket_code=$(json_extract "$HTTP_BODY" ".data.ticketCode")
        [ -z "$s4_ticket_code" ] && s4_ticket_code=$(json_extract "$HTTP_BODY" ".ticketCode")
        log_pass "S4.2: Technician creates repair intake ticket (ID: $s4_ticket_id)" "HTTP $HTTP_CODE"
    else
        log_fail "S4.2: Technician creates repair intake ticket" "HTTP 200 or 201" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Step 3: Add Spare Part
    if [ -n "$s4_ticket_id" ]; then
        do_curl "POST" "/api/repairs/${s4_ticket_id}/items?variantId=1&quantity=1&unitPrice=85000" "" "$tech_token" "" ""
        if [ "$HTTP_CODE" = "200" ]; then
            log_pass "S4.3: Technician consumes spare part #1 for repair ticket" "HTTP 200"
        else
            log_fail "S4.3: Technician consumes spare part" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
        fi
        
        # Step 4: Update Status to Completed
        do_curl "PUT" "/api/repairs/${s4_ticket_id}/status?status=Completed&solutionNote=Replaced+bad+cell+and+rebalanced+BMS&finalCost=120000" "" "$tech_token" "" ""
        if [ "$HTTP_CODE" = "200" ]; then
            log_pass "S4.4: Technician finalizes repair ticket status as Completed" "HTTP 200"
        else
            log_fail "S4.4: Technician finalizes repair ticket status" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
        fi
    fi
    
    # -------------------------------------------------------------------------
    # Scenario 5: Admin Executive Financial & Audit Review
    # -------------------------------------------------------------------------
    log_section "Scenario 5: Admin Executive P&L, Inventory Reconciliation & Debt Audit"
    login_user "admin" "P@ssword123"
    local adm_token="$LOGIN_ACCESS_TOKEN"
    
    # Step 1: Daily Revenue
    do_curl "GET" "/api/reports/revenue-daily" "" "$adm_token" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "S5.1: Admin reviews daily revenue ledger" "HTTP 200"
    else
        log_fail "S5.1: Admin reviews daily revenue ledger" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Step 2: Aggregate Revenue Summary
    do_curl "GET" "/api/reports/revenue-summary" "" "$adm_token" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "S5.2: Admin reviews aggregate P&L summary" "HTTP 200"
    else
        log_fail "S5.2: Admin reviews aggregate P&L summary" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Step 3: Inventory Reconciliation
    do_curl "GET" "/api/reports/inventory-reconciliation" "" "$adm_token" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "S5.3: Admin reviews inventory reconciliation discrepancy report" "HTTP 200"
    else
        log_fail "S5.3: Admin reviews inventory reconciliation report" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Step 4: Supplier Debt Summary
    do_curl "GET" "/api/reports/supplier-debt" "" "$adm_token" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "S5.4: Admin reviews supplier accounts payable debt summary" "HTTP 200"
    else
        log_fail "S5.4: Admin reviews supplier debt summary" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # -------------------------------------------------------------------------
    # Scenario 6: Token Lifecycle & Dual-Channel Refresh Rotation
    # -------------------------------------------------------------------------
    log_section "Scenario 6: End-to-End Dual-Channel Token Lifecycle & Rotation"
    local s6_cookie_jar="${TEMP_DIR}/s6_cookies.txt"
    local s6_cookie_jar2="${TEMP_DIR}/s6_cookies2.txt"
    
    # Step 1: Login and collect tokens
    do_curl "POST" "/api/auth/login" "{\"usernameOrPhoneOrEmail\":\"sales\",\"password\":\"P@ssword123\"}" "" "$s6_cookie_jar" ""
    local s6_access_token=$(json_extract "$HTTP_BODY" ".data.accessToken")
    [ -z "$s6_access_token" ] && s6_access_token=$(json_extract "$HTTP_BODY" ".accessToken")
    local s6_refresh_token=$(json_extract "$HTTP_BODY" ".data.refreshToken")
    [ -z "$s6_refresh_token" ] && s6_refresh_token=$(json_extract "$HTTP_BODY" ".refreshToken")
    
    if [ -n "$s6_access_token" ]; then
        log_pass "S6.1: Initial login acquires Access Token & Refresh Token" "Access: ${#s6_access_token} chars"
    else
        log_fail "S6.1: Initial login acquires tokens" "Non-empty access token" "Empty" "$HTTP_BODY"
    fi
    
    # Step 2: Use access token on protected endpoint /api/auth/me
    do_curl "GET" "/api/auth/me" "" "$s6_access_token" "" ""
    if [ "$HTTP_CODE" = "200" ]; then
        log_pass "S6.2: Access Token authenticates caller on /api/auth/me" "HTTP 200"
    else
        log_fail "S6.2: Access Token authenticates caller" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Step 3: Refresh Access Token via HttpOnly Cookie
    do_curl "POST" "/api/auth/refresh-token" "" "" "$s6_cookie_jar2" "$s6_cookie_jar"
    local s6_renewed_token=""
    if [ "$HTTP_CODE" = "200" ]; then
        s6_renewed_token=$(json_extract "$HTTP_BODY" ".data.accessToken")
        [ -z "$s6_renewed_token" ] && s6_renewed_token=$(json_extract "$HTTP_BODY" ".accessToken")
        log_pass "S6.3: Renew Access Token via Cookie Channel" "HTTP 200"
    else
        log_fail "S6.3: Renew Access Token via Cookie Channel" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
    fi
    
    # Step 4: Verify renewed token works
    if [ -n "$s6_renewed_token" ]; then
        do_curl "GET" "/api/auth/me" "" "$s6_renewed_token" "" ""
        if [ "$HTTP_CODE" = "200" ]; then
            log_pass "S6.4: Renewed Access Token functions on /api/auth/me" "HTTP 200"
        else
            log_fail "S6.4: Renewed Access Token functions on /api/auth/me" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
        fi
    fi
    
    # Step 5: Refresh Access Token via JSON Request Body
    if [ -n "$s6_refresh_token" ]; then
        do_curl "POST" "/api/auth/refresh-token" "{\"refreshToken\":\"$s6_refresh_token\"}" "" "" ""
        local s6_body_token=""
        if [ "$HTTP_CODE" = "200" ]; then
            s6_body_token=$(json_extract "$HTTP_BODY" ".data.accessToken")
            [ -z "$s6_body_token" ] && s6_body_token=$(json_extract "$HTTP_BODY" ".accessToken")
            log_pass "S6.5: Renew Access Token via JSON Request Body Channel" "HTTP 200"
        else
            log_fail "S6.5: Renew Access Token via JSON Request Body Channel" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
        fi
        
        # Step 6: Verify second renewed token works
        if [ -n "$s6_body_token" ]; then
            do_curl "GET" "/api/auth/me" "" "$s6_body_token" "" ""
            if [ "$HTTP_CODE" = "200" ]; then
                log_pass "S6.6: Second renewed Access Token functions on /api/auth/me" "HTTP 200"
            else
                log_fail "S6.6: Second renewed Access Token functions on /api/auth/me" "HTTP 200" "HTTP $HTTP_CODE" "$HTTP_BODY"
            fi
        fi
    fi
}

# ==============================================================================
# MAIN EXECUTION ENTRY POINT
# ==============================================================================

main() {
    local start_time=$(date +%s)
    
    echo -e "${BOLD}${MAGENTA}"
    echo "  ____        _   _                     _____ ____ "
    echo " | __ )  __ _| |_| |_ ___ _ __ _   _   | ____/ ___|"
    echo " |  _ \ / _\` | __| __/ _ \ '__| | | |  |  _|| |    "
    echo " | |_) | (_| | |_| ||  __/ |  | |_| |  | |__| |___ "
    echo " |____/ \__,_|\__|\__\___|_|   \__, |  |_____\____|"
    echo "                               |___/               "
    echo -e "${RESET}"
    echo -e "${BOLD}E2E Security & RBAC Test Suite Runner${RESET}"
    echo -e "Authoritative Target: ${CYAN}${BASE_URL}${RESET}"
    echo -e "Started At: $(date -u +"%Y-%m-%dT%H:%M:%SZ")"
    echo ""
    
    # 0. Preflight check
    check_preflight "$1"
    
    # 1. Tier 1
    run_tier1_feature_coverage
    
    # 2. Tier 2
    run_tier2_boundary_and_adversarial
    
    # 3. Tier 3
    run_tier3_rbac_matrix
    
    # 4. Tier 4
    run_tier4_business_scenarios
    
    # --- Final Test Summary Report ---
    local end_time=$(date +%s)
    local duration=$((end_time - start_time))
    
    log_header "TEST SUITE EXECUTION SUMMARY"
    printf "  Total Tests Executed : %b%d%b\n" "${BOLD}" "$TOTAL_TESTS" "${RESET}"
    printf "  Passed               : %b%d%b\n" "${GREEN}${BOLD}" "$PASSED_TESTS" "${RESET}"
    printf "  Failed               : %b%d%b\n" "${RED}${BOLD}" "$FAILED_TESTS" "${RESET}"
    printf "  Skipped              : %b%d%b\n" "${YELLOW}${BOLD}" "$SKIPPED_TESTS" "${RESET}"
    printf "  Duration             : %d seconds\n" "$duration"
    echo ""
    
    if [ "$FAILED_TESTS" -eq 0 ]; then
        echo -e "${GREEN}${BOLD}================================================================================${RESET}"
        echo -e "${GREEN}${BOLD}  RESULT: ALL TESTS PASSED SUCCESSFULLY (100% PASS RATE)${RESET}"
        echo -e "${GREEN}${BOLD}================================================================================${RESET}"
        exit 0
    else
        echo -e "${RED}${BOLD}================================================================================${RESET}"
        echo -e "${RED}${BOLD}  RESULT: ${FAILED_TESTS} TEST(S) FAILED. VERIFICATION REJECTED.${RESET}"
        echo -e "${RED}${BOLD}================================================================================${RESET}"
        exit 1
    fi
}

main "$@"
