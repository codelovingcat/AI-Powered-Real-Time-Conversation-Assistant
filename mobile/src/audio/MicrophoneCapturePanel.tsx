import { useEffect, useMemo, useState } from "react";
import {
  AppState,
  Linking,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  View
} from "react-native";
import * as FileSystem from "expo-file-system/legacy";
import {
  AudioModule,
  RecordingPresets,
  setAudioModeAsync,
  useAudioRecorder
} from "expo-audio";
import {
  MicrophoneCaptureController,
  type MicrophoneCaptureState
} from "./microphoneCapture";

export function MicrophoneCapturePanel() {
  const recorder = useAudioRecorder(RecordingPresets.LOW_QUALITY);
  const controller = useMemo(
    () => new MicrophoneCaptureController({
      recorder,
      getPermission: () => AudioModule.getRecordingPermissionsAsync(),
      requestPermission: () => AudioModule.requestRecordingPermissionsAsync(),
      configureRecordingMode: () => setAudioModeAsync({
        allowsRecording: true,
        playsInSilentMode: true
      }),
      deleteTemporaryFile: async (uri) => {
        await FileSystem.deleteAsync(uri, { idempotent: true });
      }
    }),
    [recorder]
  );
  const [state, setState] = useState<MicrophoneCaptureState>(controller.state);

  useEffect(() => {
    const unsubscribe = controller.subscribe(setState);
    void controller.loadPermission();

    const appStateSubscription = AppState.addEventListener("change", (nextState) => {
      void controller.onAppStateChange(nextState);
    });

    return () => {
      appStateSubscription.remove();
      unsubscribe();
      // A navigation/unmount is treated like an explicit stop. The cache file is discarded.
      void controller.stop();
    };
  }, [controller]);

  const busy = state.phase === "requestingPermission" || state.phase === "stopping";
  const onPress = () => {
    if (state.phase === "recording") {
      void controller.stop();
      return;
    }

    if (state.permission === "granted") {
      void controller.start();
      return;
    }

    if (state.canAskAgain) {
      void controller.requestPermission();
      return;
    }

    void Linking.openSettings();
  };

  const actionLabel = state.phase === "requestingPermission"
    ? "Checking permission…"
    : state.phase === "stopping"
      ? "Stopping capture…"
      : state.phase === "recording"
        ? "Stop microphone capture"
        : state.permission === "granted"
          ? "Start microphone test"
          : state.permission === "denied" && !state.canAskAgain
            ? "Open device settings"
            : "Allow microphone access";

  return (
    <View style={styles.panel}>
      <Text style={styles.heading}>Microphone</Text>
      {Platform.OS === "web" ? (
        <Text style={styles.muted}>Native microphone capture is available in the iOS and Android app.</Text>
      ) : (
        <>
          <Text style={styles.muted}>
            {state.permission === "unknown"
              ? "Checking microphone permission…"
              : state.permission === "granted"
                ? "Permission granted. Capture starts only when you press the button."
                : state.permission === "undetermined"
                  ? "Microphone access has not been requested."
                  : "Microphone access is denied or has been revoked."}
          </Text>
          {state.message ? (
            <Text
              accessibilityRole={state.phase === "error" ? "alert" : undefined}
              style={state.phase === "error" || state.permission === "denied" ? styles.error : styles.notice}
            >
              {state.message}
            </Text>
          ) : null}
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={actionLabel}
            style={[styles.button, busy && styles.disabled]}
            disabled={busy || state.permission === "unknown"}
            onPress={onPress}
          >
            <Text style={styles.buttonText}>{actionLabel}</Text>
          </Pressable>
          <Text style={styles.privacy}>
            Audio is recorded temporarily by the device and deleted when capture stops, the app backgrounds, or this panel closes. It is not uploaded or added to conversation history.
          </Text>
        </>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  panel: {
    gap: 10,
    borderWidth: 1,
    borderColor: "#d0d5dd",
    borderRadius: 14,
    padding: 14,
    backgroundColor: "#f9fafb"
  },
  heading: {
    fontSize: 16,
    fontWeight: "700"
  },
  muted: { color: "#667085" },
  notice: { color: "#175cd3" },
  error: { color: "#b42318" },
  privacy: {
    color: "#667085",
    fontSize: 12,
    lineHeight: 17
  },
  button: {
    minHeight: 46,
    borderRadius: 10,
    paddingHorizontal: 14,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#101828"
  },
  disabled: { opacity: 0.55 },
  buttonText: {
    color: "#fff",
    fontWeight: "700"
  }
});
