/** Tokens from docs/UI-Design-Notes.md. Keep names and hex values identical. */
module.exports = {
  content: ['./Pages/**/*.cshtml', './Pages/**/*.cs', './Infrastructure/**/*.cs', './wwwroot/js/**/*.js'],
  theme: {
    extend: {
      colors: {
        'brand-navy': '#1B2A41',
        'brand-navy-dark': '#12203A',
        'brand-gold': '#C9A227',
        'brand-gold-soft': '#F7EFD4',
        'bg-page': '#F4F6F9',
        'bg-surface': '#FFFFFF',
        'bg-subtle': '#EEF1F5',
        'text-primary': '#111827',
        'text-secondary': '#5B6472',
        'text-muted': '#8A94A3',
        border: '#E3E7ED',
        'border-strong': '#C9D0DA',
        info: '#2563EB',
        'info-bg': '#E8EFFD',
        success: '#15803D',
        'success-bg': '#E6F4EA',
        warning: '#B45309',
        'warning-bg': '#FDF1DC',
        danger: '#B91C1C',
        'danger-bg': '#FCE8E8',
        neutral: '#4B5563',
        'neutral-bg': '#EEF1F5'
      },
      fontFamily: { sans: ['Inter', 'system-ui', 'sans-serif'] },
      borderRadius: { card: '14px', control: '10px' },
      boxShadow: { card: '0 2px 8px rgba(17,26,41,0.06)' },
      fontSize: {
        display: ['32px', { lineHeight: '40px', fontWeight: '700' }],
        h1: ['24px', { lineHeight: '32px', fontWeight: '600' }],
        h2: ['20px', { lineHeight: '28px', fontWeight: '600' }],
        h3: ['16px', { lineHeight: '24px', fontWeight: '600' }],
        body: ['16px', { lineHeight: '24px' }],
        'body-sm': ['14px', { lineHeight: '20px' }],
        label: ['14px', { lineHeight: '20px', fontWeight: '500' }],
        badge: ['12px', { lineHeight: '16px', fontWeight: '500' }],
        caption: ['12px', { lineHeight: '16px' }]
      }
    }
  },
  plugins: []
};
