"use strict";

// Modified from the upstream WebToEpub file (https://github.com/dteviot/WebToEpub) for
// NektoTranslate in September 2026: adds RanobesParser.stripPostingDate and applies it wherever a
// chapter title is read, because ranobes.com packs the posting date into the same text as the
// title. Everything else in this file is upstream's, under the same GPL-3.0 licence.

parserFactory.register("ranobes.net", () => new RanobesNetParser());
parserFactory.register("ranobes.top", () => new RanobesNetParser());
parserFactory.register("ranobes.com", () => new RanobesParser());

class RanobesParser extends Parser {
    constructor() {
        super();
        this.minimumThrottle = 2000;
    }

    async getChapterUrls(dom, chapterUrlsUI) {
        let chapters = [];
        let tocUrl = dom.querySelector("div.r-fullstory-chapters-foot a[title='Go to table of contents']")?.href;
        if (tocUrl == null) {
            tocUrl = dom.querySelector("div.r-fullstory-chapters-foot a[title='Перейти в оглавление']")?.href;
        }
        if (tocUrl == null) {
            return [...dom.querySelectorAll("ul.chapters-scroll-list a")]
                .map(a => ({
                    sourceUrl:  a.href,
                    title: RanobesParser.stripPostingDate(a.querySelector(".title").textContent)
                })).reverse();
        }
        let options = {
            parser: this
        };
        let tocDom = (await HttpClient.wrapFetch(tocUrl, options)).responseXML;
        let urlsOfTocPages = RanobesParser.extractTocPageUrls(tocDom, tocUrl);
        return (await this.getChaptersFromAllTocPages(chapters, 
            this.extractPartialChapterList, urlsOfTocPages, chapterUrlsUI, options)).reverse();
    }

    static extractTocPageUrls(dom, baseUrl) {
        let max = RanobesParser.extractTocJson(dom)?.pages_count ?? 0;
        let tocUrls = [];
        for (let i = 1; i <= max; ++i) {
            tocUrls.push(`${baseUrl}page/${i}/`);
        }
        return tocUrls;
    }

    static extractTocJson(dom) {
        let baseURL = new URL(dom.baseURI).hostname;
        if (baseURL != "ranobes.com") {
            let startString = "window.__DATA__ = ";
            let scriptElement = [...dom.querySelectorAll("script")]
                .filter(s => s.textContent.includes(startString))[0];
            return (scriptElement != null)
                ? util.locateAndExtractJson(scriptElement.textContent, startString)
                : {chapters: [], pages_count: 0};
        } else {
            let linkElement = Math.max(...[...dom.querySelectorAll(".pages a")].map(a => parseInt(a.textContent)));
            return (Infinity == linkElement || -Infinity == linkElement)?
                {chapters: [], pages_count: 0} 
                : {chapters: [], pages_count: linkElement};
        }   
    }

    extractPartialChapterList(dom) {
        let baseURL = new URL(dom.baseURI).hostname;
        if (baseURL != "ranobes.com") {
            return RanobesParser.extractTocJson(dom).chapters.map(c => ({
                sourceUrl:  c.link,
                title: RanobesParser.stripPostingDate(c.title)
            }));
        } else {
            let Chapterlist = dom.querySelector("#dle-content");
            let RemoveNavigation = Chapterlist.querySelector(".navigation");
            Chapterlist.removeChild(RemoveNavigation);
            return util.hyperlinksToChapterList(Chapterlist).map(chapter => ({
                ...chapter,
                title: RanobesParser.stripPostingDate(chapter.title)
            }));
        }
    }

    // Ranobes packs each entry's posting date into the very same text as its chapter title, with no
    // space or markup between them - "твой друг2 августа 2022 в 09:04" is what the site sends, in the
    // JSON feed (`c.title`) just as much as in the rendered anchors. There is no earlier point to cut
    // the two apart: nothing in the source marks where the title ends and the date begins, so this
    // trims the known Russian date/time shape off the end of the finished string. Kept as one static
    // helper - shared by every branch above - rather than repeated regexes, and scoped to this parser
    // alone so no other one of the four hundred bundled parsers is touched by it.
    static stripPostingDate(title) {
        const postingDate = /\s*,?\s*\d{1,2}\s+(?:января|февраля|марта|апреля|мая|июня|июля|августа|сентября|октября|ноября|декабря)\s+\d{4}(?:\s*(?:г\.?|года))?(?:\s*,?\s*(?:в\s+)?\d{1,2}:\d{2})?\s*$/iu;
        return (title ?? "").replace(postingDate, "").trim();
    }

    findContent(dom) {
        return dom.querySelector("div#arrticle");
    }

    extractTitleImpl(dom) {
        let title = dom.querySelector("h1.title");
        util.removeChildElementsMatchingSelector(title, "span.subtitle, span[hidden]");
        return title;
    }

    extractAuthor(dom) {
        let authorLabel = dom.querySelector("span[itemprop='creator'] a");
        return authorLabel?.textContent ?? super.extractAuthor(dom);
    }

    findChapterTitle(dom) {
        let title = dom.querySelector("h1.title, div#arrticle h1, div#arrticle h2, div#arrticle h3");
        util.removeChildElementsMatchingSelector(title, "span, div");
        return title ? title.textContent : super.findChapterTitle(dom);
    }

    findCoverImageUrl(dom) {
        return util.getFirstImgSrc(dom, "div.r-fullstory-poster");
    }

    getInformationEpubItemChildNodes(dom) {
        return [...dom.querySelectorAll("div.moreless__full")];
    }

    cleanInformationNode(node) {
        util.removeChildElementsMatchingSelector(node, "a");
        return node;
    }    
}

class RanobesNetParser extends RanobesParser {
    constructor() {
        super();
        this.minimumThrottle = 8000;
    }

    async fetchChapter(url) {
        let options = {
            parser: this
        };
        return (await HttpClient.wrapFetch(url, options)).responseXML;
    }
    
    isCustomError(response) {
        if (response.responseXML.title == "Just a moment...") {
            return true;
        }
        return false;
    }

    setCustomErrorResponse(url, wrapOptions) {
        let newresp = {};
        newresp.url = url;
        newresp.wrapOptions = wrapOptions;
        newresp.response = {};
        newresp.response.url = url;
        newresp.response.status = 403;
        return newresp;
    }
}