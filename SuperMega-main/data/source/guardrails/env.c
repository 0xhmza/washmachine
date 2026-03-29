
char my_tolower(char c) {
    if (c >= 'A' && c <= 'Z') {
        return c + ('a' - 'A');  // or return c + 32;
    }
    return c;
}

// Returns 1 if 'needle' is found in 'haystack' (case-insensitive), 0 otherwise
int contains_case_insensitive(const char* haystack, const char* needle) {
    if (!haystack || !needle)
        return 0;

    for (; *haystack; haystack++) {
        const char* h = haystack;
        const char* n = needle;

        while (*h && *n && my_tolower((unsigned char)*h) == my_tolower((unsigned char)*n)) {
            h++;
            n++;
        }

        if (*n == '\0') {
            return 1;  // Match found
        }
    }

    return 0;  // No match
}


int executionguardrail() {
    // Execution Guardrail: Env Check
    LPCSTR envVarName = "{{guardrail_data_key}}";
    LPCSTR tocheck = "{{guardrail_data_value}}";
    char buffer[1024];  // NOTE: Do not make it bigger, or we have a __chkstack() dependency!
    DWORD result = GetEnvironmentVariableA(envVarName, buffer, 1024);
    if (result == 0) {
        return 6;
    }
    if (! contains_case_insensitive(buffer, tocheck)) {
        return 6;
    }
    return 0;
}

