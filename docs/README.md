# Washmachine Documentation

This directory contains the static documentation website for Washmachine, hosted on GitHub Pages.

## Pages

| Page | File | Description |
|---|---|---|
| **Home** | `index.html` | Landing page — features, philosophy, workflow overview, and use cases |
| **Getting Started** | `getting-started.html` | Installation, building from source, quick start guide, usage walkthrough |
| **CLI Reference** | `cli-reference.html` | Complete command documentation — every flag, option, and example |
| **Architecture** | `architecture.html` | Project structure, dependency graph, compilation pipeline, YAML catalog system |

## Supporting Files

- `styles.css` — Shared stylesheet for all pages (purple/gold luxury palette)
- `.nojekyll` — Disables Jekyll processing (plain HTML site)
- `PE_BACKDOORER_WORKFLOW.md` — Detailed PE backdooring workflow reference

## Local Development

Preview the site locally with any static server:

```bash
# Python
python -m http.server 8000

# Node.js
npx http-server

# PHP
php -S localhost:8000
```

Navigate to `http://localhost:8000`.

## Deployment

Deployed automatically via GitHub Pages from the `docs/` folder. Configure in repository Settings → Pages → Source: Deploy from branch, `/docs` folder.

## Updating

1. Edit the relevant HTML file
2. Ensure internal links remain valid
3. Test locally before committing
4. Commit and push to deploy
