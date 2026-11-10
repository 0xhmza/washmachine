# Presentation wording changes

This report records the wording changes made on the `clean` branch. They are limited to the items listed below and the selected terminology review; technical feature names and descriptive security terminology remain where they accurately describe behavior. No commands, configuration keys, variable names, or feature behavior were intentionally changed. Technical descriptions retain licensestub, injection, licensecave, and evasion terminology where applicable.

| Location | Previous wording or emphasis | Replacement |
| --- | --- | --- |
| README introduction | LicenseStub-loader tagline, red-team audience and informal anti-debug anecdote | Windows executable analysis and build toolkit; feature engine, templates and authorized-use context, with licensestub loader and PE injection capabilities stated explicitly. |
| README interfaces | Comparison to msfconsole | Interactive CLI for scripting and remote workflows. |
| README feature overview | “tricks” | “options”. |
| README feature engine | Focus on flagged techniques and inventing snippets | Configurable templates and snippets in the YAML playbook. |
| README donor metadata | “Steal” resources so the loader “blends in” | Copy resources, icon and metadata from a selected executable. |
| README PE injection methods | Stealth and capacity tradeoff | Capacity and PE structure implications. |
| CLI main help | LicenseStub evasion framework for red teamers | Windows executable analysis and build toolkit, explicitly including licensestub loader and evasion features. |
| CLI injection help and interactive guidance | Stealth comparisons for code caves | Existing space, capacity, section changes and execution timing. |
| Desktop PE recommendation | “stealthiest option” | Existing space without adding a PE section. |
| Web PE injection page | Inject the compiled loader | Inject licensestub, matching the operation input. The generated web bundle was updated to match. |


| README workflow and provisioning | Informal descriptions of licensepayload injection, defaults, helper downloads and interactive CLI | Direct descriptions of PE injection, configuration, provisioning and interactive setup. |
| CLI injection help | “least noisy” and “structurally obvious” | Select by licensepayload capacity and PE layout; new-section adds a section. |
| CLI interactive help and examples | Informal “drop into” and tool comparison | Start an interactive configuration session. |
| CLI catalog help | Reference tables | Describe the catalogs and their columns directly. |
| Desktop compiler screen and messages | “drop” a compiler, strip symbols to “harden” | Place a compiler in the directory; describe symbol removal and resulting size reduction. |

Verification: `git diff --check` passed. The web build and test could not run because the existing Babel installation is missing `read-input-source-map-file.js`; an ordinary `npm install --no-save --ignore-scripts` did not repair it. The web bundle's corresponding text was updated directly.

The CLI build was also attempted. It failed because existing untracked sync-conflict source files introduce duplicate definitions; these files were left in place.
