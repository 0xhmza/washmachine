// =====================================================
// WASHMACHINE CYBERPUNK THEME - INTERACTIVE EFFECTS
// =====================================================

(function() {
    'use strict';

    // ==================== SMOOTH SCROLL ====================
    document.querySelectorAll('a[href^="#"]').forEach(anchor => {
        anchor.addEventListener('click', function (e) {
            e.preventDefault();
            const target = document.querySelector(this.getAttribute('href'));
            if (target) {
                target.scrollIntoView({
                    behavior: 'smooth',
                    block: 'start'
                });
            }
        });
    });

    // ==================== TYPING EFFECT ====================
    function typeWriter(element, text, speed = 50) {
        let i = 0;
        element.textContent = '';

        function type() {
            if (i < text.length) {
                element.textContent += text.charAt(i);
                i++;
                setTimeout(type, speed);
            }
        }

        type();
    }

    // Apply typing effect to code prompts when they come into view
    const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting && !entry.target.dataset.typed) {
                entry.target.dataset.typed = 'true';
                const text = entry.target.textContent;
                typeWriter(entry.target, text, 30);
            }
        });
    }, { threshold: 0.5 });

    // Observe command elements (optional - can be enabled selectively)
    // document.querySelectorAll('.command').forEach(cmd => observer.observe(cmd));

    // ==================== MATRIX RAIN BACKGROUND ====================
    function createMatrixRain() {
        const canvas = document.createElement('canvas');
        canvas.style.position = 'fixed';
        canvas.style.top = '0';
        canvas.style.left = '0';
        canvas.style.width = '100%';
        canvas.style.height = '100%';
        canvas.style.pointerEvents = 'none';
        canvas.style.zIndex = '1';
        canvas.style.opacity = '0.05';
        document.body.insertBefore(canvas, document.body.firstChild);

        const ctx = canvas.getContext('2d');
        canvas.width = window.innerWidth;
        canvas.height = window.innerHeight;

        const chars = '01アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲン';
        const fontSize = 14;
        const columns = canvas.width / fontSize;
        const drops = [];

        for (let i = 0; i < columns; i++) {
            drops[i] = Math.random() * -100;
        }

        function draw() {
            ctx.fillStyle = 'rgba(10, 10, 15, 0.05)';
            ctx.fillRect(0, 0, canvas.width, canvas.height);

            ctx.fillStyle = '#00f5ff';
            ctx.font = fontSize + 'px monospace';

            for (let i = 0; i < drops.length; i++) {
                const text = chars[Math.floor(Math.random() * chars.length)];
                ctx.fillText(text, i * fontSize, drops[i] * fontSize);

                if (drops[i] * fontSize > canvas.height && Math.random() > 0.975) {
                    drops[i] = 0;
                }
                drops[i]++;
            }
        }

        setInterval(draw, 35);

        window.addEventListener('resize', () => {
            canvas.width = window.innerWidth;
            canvas.height = window.innerHeight;
        });
    }

    // Optionally enable matrix rain (disabled by default for performance)
    // createMatrixRain();

    // ==================== GLITCH TEXT EFFECT ====================
    function glitchEffect() {
        const glitchElements = document.querySelectorAll('.glitch');

        glitchElements.forEach(element => {
            element.addEventListener('mouseenter', () => {
                const original = element.textContent;
                const chars = '!<>-_\\/[]{}—=+*^?#________';
                let iterations = 0;
                const maxIterations = 3;

                const interval = setInterval(() => {
                    element.textContent = element.textContent
                        .split('')
                        .map((char, index) => {
                            if (index < iterations) {
                                return original[index];
                            }
                            return chars[Math.floor(Math.random() * chars.length)];
                        })
                        .join('');

                    if (iterations >= maxIterations) {
                        clearInterval(interval);
                        element.textContent = original;
                    }

                    iterations += 1/3;
                }, 30);
            });
        });
    }

    glitchEffect();

    // ==================== PARALLAX SCROLL ====================
    function parallaxScroll() {
        const scrolled = window.pageYOffset;
        const parallaxElements = document.querySelectorAll('.hero');

        parallaxElements.forEach(element => {
            const speed = 0.5;
            element.style.transform = `translateY(${scrolled * speed}px)`;
        });
    }

    window.addEventListener('scroll', parallaxScroll);

    // ==================== CARD HOVER GLOW ====================
    const cards = document.querySelectorAll('.card, .capability-card, .command-item');

    cards.forEach(card => {
        card.addEventListener('mousemove', (e) => {
            const rect = card.getBoundingClientRect();
            const x = e.clientX - rect.left;
            const y = e.clientY - rect.top;

            card.style.setProperty('--mouse-x', `${x}px`);
            card.style.setProperty('--mouse-y', `${y}px`);
        });
    });

    // ==================== TERMINAL BLINK CURSOR ====================
    function addBlinkingCursor() {
        const terminals = document.querySelectorAll('.terminal-body');

        terminals.forEach(terminal => {
            const cursor = document.createElement('span');
            cursor.textContent = '█';
            cursor.style.animation = 'blink 1s infinite';
            cursor.style.marginLeft = '2px';
            cursor.style.color = '#00f5ff';

            // Add cursor to the end of terminal content
            const lastLine = terminal.querySelector('pre');
            if (lastLine) {
                // Only add cursor if not already present
                if (!terminal.querySelector('.cursor')) {
                    cursor.className = 'cursor';
                    // lastLine.appendChild(cursor);
                }
            }
        });
    }

    // Add CSS for cursor blink
    const style = document.createElement('style');
    style.textContent = `
        @keyframes blink {
            0%, 49% { opacity: 1; }
            50%, 100% { opacity: 0; }
        }

        .card::before,
        .capability-card::before,
        .command-item::before {
            content: '';
            position: absolute;
            width: 100px;
            height: 100px;
            border-radius: 50%;
            background: radial-gradient(circle, rgba(0, 245, 255, 0.1) 0%, transparent 70%);
            pointer-events: none;
            top: var(--mouse-y, -100px);
            left: var(--mouse-x, -100px);
            transform: translate(-50%, -50%);
            opacity: 0;
            transition: opacity 0.3s;
        }

        .card:hover::before,
        .capability-card:hover::before,
        .command-item:hover::before {
            opacity: 1;
        }
    `;
    document.head.appendChild(style);

    // ==================== CODE COPY FUNCTIONALITY ====================
    function addCopyButtons() {
        const codeBlocks = document.querySelectorAll('.terminal-body pre');

        codeBlocks.forEach(block => {
            const button = document.createElement('button');
            button.className = 'copy-btn';
            button.textContent = 'COPY';
            button.style.cssText = `
                position: absolute;
                top: 10px;
                right: 10px;
                background: #00f5ff;
                color: #0a0a0f;
                border: none;
                padding: 0.5rem 1rem;
                font-family: 'Courier New', monospace;
                font-size: 0.8rem;
                cursor: pointer;
                opacity: 0;
                transition: opacity 0.3s;
            `;

            const parent = block.parentElement;
            parent.style.position = 'relative';
            parent.appendChild(button);

            parent.addEventListener('mouseenter', () => {
                button.style.opacity = '1';
            });

            parent.addEventListener('mouseleave', () => {
                button.style.opacity = '0';
            });

            button.addEventListener('click', () => {
                const text = block.textContent;
                navigator.clipboard.writeText(text).then(() => {
                    button.textContent = 'COPIED!';
                    setTimeout(() => {
                        button.textContent = 'COPY';
                    }, 2000);
                });
            });
        });
    }

    addCopyButtons();

    // ==================== SCROLL ANIMATIONS ====================
    const observerOptions = {
        threshold: 0.1,
        rootMargin: '0px 0px -100px 0px'
    };

    const fadeInObserver = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.style.opacity = '1';
                entry.target.style.transform = 'translateY(0)';
            }
        });
    }, observerOptions);

    // Apply fade-in animation to sections
    document.querySelectorAll('.card, .capability-card, .command-item').forEach(el => {
        el.style.opacity = '0';
        el.style.transform = 'translateY(20px)';
        el.style.transition = 'opacity 0.6s ease, transform 0.6s ease';
        fadeInObserver.observe(el);
    });

    // ==================== ACTIVE NAV LINK ====================
    function updateActiveNavLink() {
        const sections = document.querySelectorAll('section[id]');
        const navLinks = document.querySelectorAll('.nav-links a');

        window.addEventListener('scroll', () => {
            let current = '';

            sections.forEach(section => {
                const sectionTop = section.offsetTop;
                const sectionHeight = section.clientHeight;
                if (pageYOffset >= sectionTop - 200) {
                    current = section.getAttribute('id');
                }
            });

            navLinks.forEach(link => {
                link.classList.remove('active');
                if (link.getAttribute('href') === `#${current}`) {
                    link.classList.add('active');
                }
            });
        });
    }

    // Only update active link if on a page with sections
    if (document.querySelectorAll('section[id]').length > 0) {
        updateActiveNavLink();
    }

    // ==================== RANDOM TECH FACTS ====================
    const techFacts = [
        "SYSTEM INITIALIZED",
        "NEURAL NETWORK ACTIVE",
        "QUANTUM ENCRYPTION ENABLED",
        "FIREWALL STATUS: OPERATIONAL",
        "ANALYZING CODE PATTERNS...",
        "COMPILING BINARY DATA...",
        "SHELLCODE ENCRYPTED",
        "PROCESS INJECTION READY"
    ];

    function displayRandomFact() {
        const factElement = document.createElement('div');
        factElement.style.cssText = `
            position: fixed;
            bottom: 20px;
            right: 20px;
            background: rgba(0, 245, 255, 0.1);
            border: 1px solid #00f5ff;
            padding: 1rem;
            font-family: 'Courier New', monospace;
            color: #00f5ff;
            font-size: 0.8rem;
            z-index: 9999;
            animation: slideIn 0.5s ease;
            max-width: 300px;
        `;

        const fact = techFacts[Math.floor(Math.random() * techFacts.length)];
        factElement.textContent = `[SYSTEM] ${fact}`;

        const slideInStyle = document.createElement('style');
        slideInStyle.textContent = `
            @keyframes slideIn {
                from {
                    transform: translateX(400px);
                    opacity: 0;
                }
                to {
                    transform: translateX(0);
                    opacity: 1;
                }
            }
            @keyframes slideOut {
                from {
                    transform: translateX(0);
                    opacity: 1;
                }
                to {
                    transform: translateX(400px);
                    opacity: 0;
                }
            }
        `;
        document.head.appendChild(slideInStyle);

        document.body.appendChild(factElement);

        setTimeout(() => {
            factElement.style.animation = 'slideOut 0.5s ease';
            setTimeout(() => {
                factElement.remove();
            }, 500);
        }, 3000);
    }

    // Display a random fact on page load
    setTimeout(displayRandomFact, 1000);

    // ==================== CONSOLE EASTER EGG ====================
    console.log('%c WASHMACHINE v1.0 ', 'background: #00f5ff; color: #0a0a0f; font-size: 20px; font-weight: bold; padding: 10px;');
    console.log('%c Shellcode Loader Builder Framework ', 'background: #ff006e; color: #fff; font-size: 14px; padding: 5px;');
    console.log('%c Designed with cyberpunk aesthetics 🌆⚡ ', 'color: #00f5ff; font-size: 12px;');

    // ==================== PREVENT RIGHT CLICK (OPTIONAL) ====================
    // Uncomment to disable right-click context menu
    // document.addEventListener('contextmenu', e => e.preventDefault());

    // ==================== PERFORMANCE MONITOR ====================
    if (window.performance) {
        window.addEventListener('load', () => {
            setTimeout(() => {
                const perfData = window.performance.timing;
                const pageLoadTime = perfData.loadEventEnd - perfData.navigationStart;
                console.log(`%c Page loaded in ${pageLoadTime}ms `, 'color: #39ff14; font-weight: bold;');
            }, 0);
        });
    }

    // ==================== INITIALIZE ====================
    console.log('%c[SYSTEM] All cyberpunk effects initialized ✓', 'color: #39ff14;');

})();
