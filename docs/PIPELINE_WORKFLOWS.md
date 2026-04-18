# Pipeline Workflows

```mermaid
flowchart TD
    A([Start]) --> B{Input source}

    B -->|Existing .exe carrier| C[Strip .exe to flat .bin]
    B -->|Raw/.bin shellcode| D[Use shellcode .bin]
    B -->|URL/web payload mode| E[Use web payload source]

    C --> F{Enable Shikata Ga Nai?}
    D --> F
    E --> G[Skip SGN for web payload source]

    F -->|Yes| H[Shikata Ga Nai encode\n-c <count>, -M <max bytes>]
    F -->|No| I[Pass shellcode unchanged]

    H --> J[Bin2Shell encode/envelope]
    I --> J
    G --> J

    J --> K[Template-based configuration\n(snippets/placeholders)]
    K --> L[Compile loader]
    L --> M{Strip compiled loader to .bin?}

    M -->|Yes| N[Strip compiled loader]
    M -->|No| O[Keep compiled loader]

    N --> P{Backdoor target PE?}
    O --> P

    P -->|No| Q([Output artifact])
    P -->|Yes| R[Backdoor target PE]

    R --> S{Recompile stage required?}
    S -->|Yes| T[Compile final stage]
    S -->|No| Q

    T --> Q
```

Reference order with SGN enabled:

`.exe -> strip -> shikata ga nai (optional) -> bin2shell -> template based config -> compile -> strip -> backdoor -> compile`
