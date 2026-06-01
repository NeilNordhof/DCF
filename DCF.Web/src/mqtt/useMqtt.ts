import mqtt from 'mqtt';
import type { MqttClient } from 'mqtt';
import { useEffect, useRef, useState } from 'react';

const MQTT_URL = import.meta.env.VITE_MQTT_URL as string;

export function useMqtt<T>(topic: string) {
  const [message, setMessage] = useState<T | null>(null);
  const clientRef = useRef<MqttClient | null>(null);

  useEffect(() => {
    let cancelled = false;
    const client = mqtt.connect(MQTT_URL);
    clientRef.current = client;

    client.on('connect', () => {
      if (cancelled) return;
      client.subscribe(topic);
    });
    client.on('message', (_topic, payload) => {
      try {
        setMessage(JSON.parse(payload.toString()) as T);
      } catch {
        // ignore malformed messages
      }
    });
    client.on('error', () => { /* connection errors are non-fatal */ });

    return () => {
      cancelled = true;
      client.end();
    };
  }, [topic]);

  return message;
}
