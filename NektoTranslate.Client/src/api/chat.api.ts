import type { ChatMessage, ChatRole } from "@/types/models/domain";

import { decodeChatRole } from "@/utils/wire";
import { apiClient } from "./client";


interface RawChatMessage extends Omit<ChatMessage, "role"> {
    role: number | string;
}


function toMessage(raw: RawChatMessage): ChatMessage {
    return { ...raw, role: decodeChatRole(raw.role) as ChatRole };
}


export const chatApi = {
    history(novelId: number): Promise<ChatMessage[]> {
        return apiClient<RawChatMessage[]>(`/api/novels/${novelId}/chat`).then(rows => rows.map(toMessage));
    },

    // Returns every message the turn produced, tool notes included, rather than just the reply. One
    // request in, a transcript fragment out.
    send(novelId: number, text: string): Promise<ChatMessage[]> {
        return apiClient<RawChatMessage[]>(`/api/novels/${novelId}/chat`, {
            method: "POST",
            body: { text },
        }).then(rows => rows.map(toMessage));
    },
};
