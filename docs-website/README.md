# Washmachine Documentation Website

> A complete static documentation website with cyberpunk theme for the Washmachine shellcode loader builder framework.

## Overview

This is a comprehensive, self-contained static website documenting the entire Washmachine framework. The website features:

- **Cyberpunk-themed design** with neon colors, glitch effects, and terminal aesthetics
- **Complete documentation** covering all aspects of the framework
- **Interactive elements** including smooth scrolling, hover effects, and animations
- **Responsive layout** that works on desktop, tablet, and mobile devices
- **Static HTML/CSS/JS** - no build process required, just open in a browser

## Structure

```
docs-website/
├── index.html           # Homepage with overview and quick start
├── features.html        # Complete feature breakdown
├── cli.html            # CLI command reference
├── yaml.html           # YAML catalog documentation
├── architecture.html    # System architecture and design
├── building.html       # Build and deployment guide
├── css/
│   └── cyberpunk.css   # Complete cyberpunk theme stylesheet
├── js/
│   └── cyberpunk.js    # Interactive effects and animations
└── README.md           # This file
```

## Pages

### 1. Home (index.html)
- Hero section with neon text effects
- Quick start guide
- Core capabilities overview
- Available commands
- Architecture diagram

### 2. Features (features.html)
- Shellcode sources
- Web payload wizard
- Templates
- Snippet sections
- Encoding options
- Compilation pipeline

### 3. CLI Reference (cli.html)
- Complete command documentation
- Usage examples
- All command options
- Terminal examples

### 4. YAML Catalog (yaml.html)
- Template structure
- Section configuration
- Input controls
- System placeholders
- Complete examples

### 5. Architecture (architecture.html)
- Project structure
- Dependency graph
- Compilation pipeline
- Design patterns
- Key decisions

### 6. Building (building.html)
- Requirements
- Development builds
- Publishing for release
- Deployment guide
- Troubleshooting

## Design Features

### Cyberpunk Theme
- **Color Palette**: Neon cyan (#00f5ff), pink (#ff006e), purple (#8b00ff), green (#39ff14)
- **Typography**: Monospace fonts for code/terminal, sans-serif for body text
- **Effects**: Scanlines, glitch text, neon glow, grid backgrounds
- **Animations**: Smooth transitions, hover effects, scroll animations

### Interactive Elements
- Smooth scroll navigation
- Glitch effect on logo
- Hover glow on cards
- Copy buttons for code blocks
- Terminal-style code displays
- Responsive navigation

### Accessibility
- Semantic HTML structure
- ARIA labels where appropriate
- Keyboard navigation support
- High contrast color scheme
- Readable font sizes

## Usage

### Local Development
Simply open `index.html` in a web browser:

```bash
# On Windows
start index.html

# On macOS
open index.html

# On Linux
xdg-open index.html
```

Or use a local web server:

```bash
# Python 3
python -m http.server 8000

# Node.js (with http-server)
npx http-server

# Then open http://localhost:8000
```

### Deployment

To deploy to a web server:

1. Upload the entire `docs-website/` directory
2. Ensure all files maintain their relative paths
3. Configure web server to serve `index.html` as the default page

**GitHub Pages:**
```bash
# Copy to gh-pages branch or docs/ folder
cp -r docs-website/* docs/
git add docs/
git commit -m "Update documentation website"
git push
```

**Netlify/Vercel:**
- Simply drag and drop the `docs-website/` folder
- Or connect your repository and set build directory to `docs-website/`

## Customization

### Colors
Edit CSS variables in `css/cyberpunk.css`:

```css
:root {
    --cyber-pink: #ff006e;
    --cyber-cyan: #00f5ff;
    --cyber-purple: #8b00ff;
    --cyber-green: #39ff14;
    /* ... */
}
```

### Effects
Enable/disable effects in `js/cyberpunk.js`:

```javascript
// Matrix rain background (disabled by default)
createMatrixRain();

// Random tech facts notification
displayRandomFact();
```

### Content
All content is in static HTML files. Edit the HTML directly to update:
- Text content
- Code examples
- Documentation
- Links

## Browser Support

- Chrome/Edge 90+
- Firefox 88+
- Safari 14+
- Opera 76+

All modern browsers with ES6 support.

## Performance

- Lightweight: ~100KB total (HTML + CSS + JS)
- No external dependencies
- Optimized animations
- Minimal JavaScript
- Fast load times

## Credits

- Design: Custom cyberpunk theme
- Framework: Washmachine by 0xhmza
- Icons: Unicode/Emoji
- Fonts: System fonts (no external font loading)

## License

Documentation content follows the same license as the Washmachine project.

---

**Generated with cyberpunk aesthetics** ⚡🌆
