type SessionExpiredListener = () => void;

const sessionExpiredListeners = new Set<SessionExpiredListener>();

export function subscribeToSessionExpired(
  listener: SessionExpiredListener
): () => void {
  sessionExpiredListeners.add(listener);
  return () => sessionExpiredListeners.delete(listener);
}

export function notifySessionExpired(): void {
  for (const listener of sessionExpiredListeners) {
    listener();
  }
}
