# Washmachine Documentation

This directory contains the static documentation website for Washmachine, hosted on GitHub Pages.

## Structure

- `index.html` - Landing page with overview and features
- `getting-started.html` - Installation and setup guide
- `cli-reference.html` - Complete CLI command documentation
- `architecture.html` - Project architecture and design documentation
- `styles.css` - Shared stylesheet for all pages
- `.nojekyll` - Disables Jekyll processing (we use plain HTML)

## Local Development

To preview the site locally, you can use any static web server. For example:

```bash
# Using Python
python -m http.server 8000

# Using Node.js http-server
npx http-server

# Using PHP
php -S localhost:8000
```

Then navigate to `http://localhost:8000` in your browser.

## Deployment

This documentation is automatically deployed to GitHub Pages when changes are pushed to the repository. Configure GitHub Pages to serve from the `docs` folder in your repository settings.

## Updating Documentation

The documentation pages are plain HTML files. To update:

1. Edit the relevant HTML file in the `docs/` directory
2. Ensure all internal links remain valid
3. Test locally before committing
4. Commit and push changes to deploy
