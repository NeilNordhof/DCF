import mqtt from 'mqtt';
import type { MqttClient } from 'mqtt';
import { useCallback, useEffect, useRef } from 'react';

const MQTT_URL = import.meta.env.VITE_MQTT_URL as string;

export function useDraftPresence(leagueId: string, userId: string | undefined) {
  const clientRef = useRef<MqttClient | null>(null);

  useEffect(() => {
    if (!userId) return;

    const presenceTopic = `dcf/leagues/${leagueId}/draft/presence`;
    const onlinePayload = JSON.stringify({ userId, status: 'online' });
    const offlinePayload = JSON.stringify({ userId, status: 'offline' });

    const client = mqtt.connect(MQTT_URL, {
      will: {
        topic: presenceTopic,
        payload: offlinePayload,
        qos: 1,
        retain: false,
      },
    });

    clientRef.current = client;

    client.on('connect', () => {
      client.publish(presenceTopic, onlinePayload, { qos: 1 });
    });

    client.on('error', () => { /* connection errors are non-fatal */ });

    // Publish online every 30s as an application-level heartbeat.
    // This keeps the broker connection alive independently of mqtt.js's internal
    // PINGREQ mechanism, which Chrome may throttle after ~5 minutes of no user
    // interaction with the tab.
    const heartbeat = setInterval(() => {
      if (client.connected) {
        client.publish(presenceTopic, onlinePayload, { qos: 0 });
      }
    }, 30_000);

    return () => {
      clearInterval(heartbeat);
      if (client.connected) {
        client.publish(presenceTopic, offlinePayload, { qos: 1 });
      }
      client.end();
      clientRef.current = null;
    };
  }, [leagueId, userId]);

  const publishPickPreview = useCallback(
    (corpsId: string, caption: string) => {
      const client = clientRef.current;
      if (!client?.connected || !userId) return;
      client.publish(
        `dcf/leagues/${leagueId}/draft/pick`,
        JSON.stringify({ userId, corpsId, caption }),
        { qos: 0 },
      );
    },
    [leagueId, userId],
  );

  return { publishPickPreview };
}
