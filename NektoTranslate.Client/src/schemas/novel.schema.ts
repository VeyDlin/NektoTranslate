import { z } from "zod";


// Backed by a searchable list in the UI (`CreateNovelModal`'s `create-item` select), but not a closed
// one: the model reads whatever lands here as a language name, so typing one that is not on the list
// still works — which is exactly the case this application exists for.
export const createNovelSchema = z.object({
    title: z.string().min(1, "Give the novel a title").max(500),
    sourceLanguage: z.string().min(1, "Name the language it is written in").max(32),
    targetLanguage: z.string().min(1, "Name the language to translate into").max(32),
    styleGuide: z.string().max(4000).optional(),
});

export type CreateNovelInput = z.infer<typeof createNovelSchema>;


export const importChaptersSchema = z.object({
    title: z.string().min(1, "Give the chapter a title").max(1000),
    body: z.string().min(1, "Paste the chapter text"),
});

export type ImportChaptersInput = z.infer<typeof importChaptersSchema>;
