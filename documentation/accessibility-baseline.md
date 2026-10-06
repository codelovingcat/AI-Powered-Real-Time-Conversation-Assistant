# Accessibility baseline

This document records the current WCAG-oriented accessibility baseline for the Conversa web experience.

## Keyboard and semantics

- Native buttons, inputs, textareas, forms, headings and landmarks are used for primary controls.
- Interactive controls expose accessible names through visible labels or explicit ARIA labels.
- Assistant input modes use the tab semantics already present in the UI and expose the selected state.
- Authentication and conversation actions remain ordinary keyboard-operable HTML controls.

## Dynamic status announcements

- Processing, connection and microphone states use polite status announcements so routine updates do not interrupt the user.
- User-actionable errors use `role="alert"` with assertive announcement semantics.
- Message-history loading and transcript updates are exposed through live regions.
- Visual status indicators are marked decorative when their text already communicates the state.

## Safe rendering

Conversation and AI text is rendered as text content rather than injected HTML. Accessibility work must not introduce `dangerouslySetInnerHTML` or another unsafe HTML rendering path.

## Reduced motion and contrast

The visual stylesheet should continue to respect the user's reduced-motion preference and maintain readable foreground/background contrast for primary flows. These checks remain part of the release accessibility review.

## Regression checks

Run the web typecheck and test suite before merging accessibility changes:

```bash
cd frontend
npm run typecheck
npm test -- --run
```

This baseline is intentionally scoped to the current web experience. Mobile accessibility requirements are tracked separately with the mobile roadmap.
