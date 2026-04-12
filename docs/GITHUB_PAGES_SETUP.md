# GitHub Pages Setup Instructions

This document provides step-by-step instructions for enabling GitHub Pages for the Washmachine documentation website.

## What Has Been Added

A complete static documentation website has been created in the `docs/` directory with:

- **index.html** - Landing page with project overview and features
- **getting-started.html** - Installation and setup guide
- **cli-reference.html** - Complete CLI command documentation
- **architecture.html** - Project architecture and design details
- **styles.css** - Professional styling for all pages
- **.nojekyll** - Disables Jekyll processing (we use plain HTML)
- **README.md** - Documentation maintenance guide

## Enabling GitHub Pages

Follow these steps to publish the documentation website:

### Step 1: Navigate to Repository Settings

1. Go to your GitHub repository: https://github.com/0xhmza/washmachine
2. Click on **Settings** (near the top right of the page)

### Step 2: Configure GitHub Pages

1. In the left sidebar, scroll down and click on **Pages** (under "Code and automation")
2. Under **Source**, select **Deploy from a branch**
3. Under **Branch**:
   - Select the branch: **main** (or the branch you want to deploy from)
   - Select the folder: **/docs**
   - Click **Save**

### Step 3: Wait for Deployment

1. GitHub will automatically build and deploy your site
2. This usually takes 1-2 minutes
3. Once deployed, you'll see a message like: "Your site is live at https://0xhmza.github.io/washmachine/"

### Step 4: Verify the Site

1. Click on the URL provided or navigate to: https://0xhmza.github.io/washmachine/
2. You should see the Washmachine documentation landing page
3. Test the navigation links to ensure all pages are working

## Alternative: Using GitHub Actions (Optional)

If you prefer using GitHub Actions for more control over the build process:

1. Go to **Settings** → **Pages**
2. Under **Source**, select **GitHub Actions**
3. GitHub will suggest a workflow for static HTML

However, since we're using plain HTML/CSS, the simpler "Deploy from a branch" method is recommended.

## Custom Domain (Optional)

If you want to use a custom domain:

1. Go to **Settings** → **Pages**
2. Under **Custom domain**, enter your domain (e.g., docs.washmachine.com)
3. Follow GitHub's instructions for DNS configuration
4. Click **Save**

## Updating the Documentation

To update the documentation:

1. Edit the HTML files in the `docs/` directory
2. Commit and push your changes
3. GitHub Pages will automatically redeploy within 1-2 minutes

## Local Testing

To test the documentation locally before deploying:

```bash
# Using Python
cd docs
python -m http.server 8000

# Using Node.js
cd docs
npx http-server

# Using PHP
cd docs
php -S localhost:8000
```

Then navigate to `http://localhost:8000` in your browser.

## Troubleshooting

### Site Not Appearing

- Check that GitHub Pages is enabled in Settings → Pages
- Verify that the branch and folder are correctly set to **main** and **/docs**
- Wait a few minutes for the initial deployment
- Check the **Actions** tab for any deployment errors

### 404 Errors

- Ensure all internal links use relative paths (e.g., `getting-started.html` not `/getting-started.html`)
- Verify that all HTML files are in the `docs/` directory
- Check that file names match the links exactly (case-sensitive on GitHub)

### Styling Not Applied

- Verify that `styles.css` exists in the `docs/` directory
- Check that all HTML files link to `styles.css` correctly
- Clear your browser cache and refresh

## File Structure

```
washmachine/
├── docs/
│   ├── index.html              # Landing page
│   ├── getting-started.html    # Getting started guide
│   ├── cli-reference.html      # CLI documentation
│   ├── architecture.html       # Architecture docs
│   ├── styles.css              # Shared stylesheet
│   ├── .nojekyll               # Disables Jekyll
│   └── README.md               # Docs maintenance guide
├── README.md                   # Main project README (now links to docs site)
└── ... (other project files)
```

## Summary

Once GitHub Pages is enabled:

✅ Documentation will be available at: https://0xhmza.github.io/washmachine/
✅ Updates to the docs/ directory will automatically redeploy
✅ The main README.md now links to the documentation website
✅ Users can easily find and navigate the full documentation

## Next Steps

After enabling GitHub Pages:

1. Share the documentation URL with users
2. Update any external links or references to point to the new documentation site
3. Consider adding a custom domain if desired
4. Keep the documentation updated as the project evolves
