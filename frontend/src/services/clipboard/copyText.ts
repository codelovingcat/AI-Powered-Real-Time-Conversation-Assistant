export async function copyText(value: string): Promise<void> {
  if (!value) {
    throw new Error("Nothing to copy.");
  }

  let clipboardError: unknown = null;

  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(value);
      return;
    } catch (error: unknown) {
      clipboardError = error;
    }
  }

  const textarea = document.createElement("textarea");
  textarea.value = value;
  textarea.setAttribute("readonly", "");
  textarea.style.position = "fixed";
  textarea.style.opacity = "0";
  textarea.style.pointerEvents = "none";
  document.body.appendChild(textarea);
  textarea.select();

  try {
    if (!document.execCommand("copy")) {
      throw clipboardError instanceof Error
        ? clipboardError
        : new Error("Clipboard access is unavailable.");
    }
  } finally {
    textarea.remove();
  }
}
