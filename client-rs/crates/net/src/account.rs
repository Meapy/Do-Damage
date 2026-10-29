//! The account server's XML answers (`App/Controllers`, `Shared/database` `ToXml`).

/// An `<Error>` answer, or XML we couldn't read.
pub fn error_text(xml: &str) -> Option<String> {
    let doc = roxmltree::Document::parse(xml).ok()?;
    let root = doc.root_element();
    (root.tag_name().name() == "Error").then(|| root.text().unwrap_or("").trim().to_owned())
}

/// A `<Success/>` answer (register, delete), or the `<Error>`'s text.
pub fn success(xml: &str) -> Result<(), String> {
    if let Some(e) = error_text(xml) {
        return Err(e);
    }
    let doc = roxmltree::Document::parse(xml).map_err(|e| e.to_string())?;
    if doc.root_element().has_tag_name("Success") { Ok(()) } else { Err("unexpected answer".into()) }
}

#[derive(Debug, Clone, PartialEq)]
pub struct ServerEntry {
    pub name: String,
    pub host: String,
    pub port: u16,
    pub admin_only: bool,
    /// How full it is, players over the maximum (`<Usage>`, 0 to 1).
    pub usage: f32,
    /// The player count the server gives ("3/85", or "- NEW!" just after it starts).
    pub usage_text: String,
}

#[derive(Debug, Clone, PartialEq)]
pub struct CharSummary {
    pub id: i32,
    pub object_type: u16,
    pub level: i32,
    pub dead: bool,
    pub exp: i32,
    /// `<CurrentFame>`.
    pub fame: i32,
    /// Item types, -1 for empty; the first four are worn.
    pub equipment: Vec<i32>,
    /// Each slot's item data (`<ItemDatas>`, JSON: its imprints), as `equipment`.
    pub item_datas: Vec<String>,
    pub tex1: i32,
    pub tex2: i32,
    /// The skin (`<Texture>`), 0 for none.
    pub skin: i32,
}

#[derive(Debug, Clone, PartialEq, Default)]
pub struct CharList {
    pub next_char_id: i32,
    pub max_num_chars: i32,
    pub chars: Vec<CharSummary>,
    pub servers: Vec<ServerEntry>,
    /// `<Account>`: the player's id, name, gold and fame.
    pub account_id: i32,
    pub name: String,
    pub credits: i32,
    pub fame: i32,
    /// What the next character slot costs, and in what (0 gold, 1 fame).
    pub next_slot_price: i32,
    pub next_slot_currency: i32,
    /// `<OwnedSkins>`.
    pub owned_skins: Vec<u16>,
    /// `<ClassStats>`: the best level and fame reached with each class.
    pub class_stats: std::collections::HashMap<u16, (i32, i32)>,
    /// `<News>`: the server's news and the account's dead characters, newest first.
    pub news: Vec<NewsItem>,
}

/// One `<News><Item>`. A dead character's links to its fame summary: `fame:<char id>`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct NewsItem {
    pub icon: String,
    pub title: String,
    pub tagline: String,
    pub link: String,
    /// Unix time.
    pub date: i64,
}

impl NewsItem {
    /// The dead character's id, for a death (`SavedNewsItem.isCharDeath`).
    pub fn death_char_id(&self) -> Option<i32> {
        self.link.strip_prefix("fame:").and_then(|id| id.trim().parse().ok())
    }
}

fn child_text<'a>(node: roxmltree::Node<'a, 'a>, name: &str) -> Option<&'a str> {
    node.children().find(|c| c.has_tag_name(name)).and_then(|c| c.text()).map(str::trim)
}

fn parse_int(s: &str) -> Option<i64> {
    let s = s.trim();
    match s.strip_prefix("0x").or_else(|| s.strip_prefix("0X")) {
        Some(hex) => i64::from_str_radix(hex, 16).ok(),
        None => s.parse().ok(),
    }
}

/// An `<Equipment>` list: item types, -1 for an empty slot. Types are 16-bit: the server writes
/// the legends' as signed numbers, so the types from 0x8000 up (the Doomed Realms items) come as
/// negative ones, and empty slots as 65535 or -1.
fn parse_items(list: Option<&str>) -> Vec<i32> {
    list.map(|l| {
        l.split(',')
            .map(|i| match parse_int(i).map(|v| v as u16) {
                Some(t) if t != 0xFFFF => t as i32,
                _ => -1,
            })
            .collect()
    })
    .unwrap_or_default()
}

/// `/char/list`.
pub fn parse_char_list(xml: &str) -> Result<CharList, String> {
    if let Some(e) = error_text(xml) {
        return Err(e);
    }
    let doc = roxmltree::Document::parse(xml).map_err(|e| e.to_string())?;
    let root = doc.root_element();
    if !root.has_tag_name("Chars") {
        return Err(format!("expected <Chars>, got <{}>", root.tag_name().name()));
    }
    let attr = |n: &str| root.attribute(n).and_then(parse_int).unwrap_or(0) as i32;
    let mut list = CharList { next_char_id: attr("nextCharId"), max_num_chars: attr("maxNumChars"), ..Default::default() };
    for c in root.children().filter(|c| c.has_tag_name("Char")) {
        let int = |n: &str| child_text(c, n).and_then(parse_int).unwrap_or(0);
        list.chars.push(CharSummary {
            id: c.attribute("id").and_then(parse_int).unwrap_or(0) as i32,
            object_type: int("ObjectType") as u16,
            level: int("Level") as i32,
            dead: child_text(c, "Dead").is_some_and(|d| d.eq_ignore_ascii_case("true")),
            exp: int("Exp") as i32,
            fame: int("CurrentFame") as i32,
            equipment: parse_items(child_text(c, "Equipment")),
            item_datas: parse_datas(child_text(c, "ItemDatas")),
            tex1: int("Tex1") as i32,
            tex2: int("Tex2") as i32,
            skin: int("Texture") as i32,
        });
    }
    if let Some(account) = root.children().find(|c| c.has_tag_name("Account")) {
        let int = |n: &str| child_text(account, n).and_then(parse_int).unwrap_or(0) as i32;
        list.account_id = int("AccountId");
        list.name = child_text(account, "Name").unwrap_or("").to_owned();
        list.credits = int("Credits");
        list.next_slot_price = int("NextCharSlotPrice");
        list.next_slot_currency = int("NextCharSlotCurrency");
        if let Some(stats) = account.children().find(|c| c.has_tag_name("Stats")) {
            list.fame = child_text(stats, "Fame").and_then(parse_int).unwrap_or(0) as i32;
            for cs in stats.children().filter(|c| c.has_tag_name("ClassStats")) {
                let t = cs.attribute("objectType").and_then(parse_int).unwrap_or(0) as u16;
                let best = |n: &str| child_text(cs, n).and_then(parse_int).unwrap_or(0) as i32;
                list.class_stats.insert(t, (best("BestLevel"), best("BestFame")));
            }
        }
    }
    if let Some(skins) = child_text(root, "OwnedSkins") {
        list.owned_skins = skins.split(',').filter_map(|s| parse_int(s).map(|v| v as u16)).collect();
    }
    if let Some(news) = root.children().find(|c| c.has_tag_name("News")) {
        for i in news.children().filter(|c| c.has_tag_name("Item")) {
            let text = |n: &str| child_text(i, n).unwrap_or("").to_owned();
            list.news.push(NewsItem {
                icon: text("Icon"),
                title: text("Title"),
                tagline: text("TagLine"),
                link: text("Link"),
                date: child_text(i, "Date").and_then(parse_int).unwrap_or(0),
            });
        }
    }
    if let Some(servers) = root.children().find(|c| c.has_tag_name("Servers")) {
        for s in servers.children().filter(|c| c.has_tag_name("Server")) {
            list.servers.push(ServerEntry {
                name: child_text(s, "Name").unwrap_or("").to_owned(),
                host: child_text(s, "DNS").unwrap_or("").to_owned(),
                port: child_text(s, "Port").and_then(parse_int).unwrap_or(2050) as u16,
                admin_only: child_text(s, "AdminOnly").is_some_and(|a| a.eq_ignore_ascii_case("true")),
                usage: child_text(s, "Usage").and_then(|u| u.parse().ok()).unwrap_or(0.0),
                usage_text: child_text(s, "UsageText").unwrap_or("").to_owned(),
            });
        }
    }
    Ok(list)
}

/// One line of `/fame/list` (the legends).
#[derive(Debug, Clone, PartialEq)]
pub struct Legend {
    pub account_id: i32,
    pub char_id: i32,
    pub name: String,
    pub object_type: u16,
    pub tex1: i32,
    pub tex2: i32,
    pub skin: i32,
    pub equipment: Vec<i32>,
    pub item_datas: Vec<String>,
    pub total_fame: i32,
}

/// `<ItemDatas>`: each slot's data, `;` between (`ToCommaDotSepString`).
fn parse_datas(s: Option<&str>) -> Vec<String> {
    s.map(|s| s.split(';').map(str::to_owned).collect()).unwrap_or_default()
}

/// `/fame/list`.
pub fn parse_fame_list(xml: &str) -> Result<Vec<Legend>, String> {
    if let Some(e) = error_text(xml) {
        return Err(e);
    }
    let doc = roxmltree::Document::parse(xml).map_err(|e| e.to_string())?;
    let root = doc.root_element();
    Ok(root
        .children()
        .filter(|c| c.has_tag_name("FameListElem"))
        .map(|e| {
            let int = |n: &str| child_text(e, n).and_then(parse_int).unwrap_or(0);
            Legend {
                account_id: e.attribute("accountId").and_then(parse_int).unwrap_or(0) as i32,
                char_id: e.attribute("charId").and_then(parse_int).unwrap_or(0) as i32,
                name: child_text(e, "Name").unwrap_or("").to_owned(),
                object_type: int("ObjectType") as u16,
                tex1: int("Tex1") as i32,
                tex2: int("Tex2") as i32,
                skin: int("Texture") as i32,
                equipment: parse_items(child_text(e, "Equipment")),
                item_datas: parse_datas(child_text(e, "ItemDatas")),
                total_fame: int("TotalFame") as i32,
            }
        })
        .collect())
}

/// One line of `/fame/recent`: a death on the server, newest first.
#[derive(Debug, Clone, PartialEq)]
pub struct RecentDeath {
    pub account_id: i32,
    pub char_id: i32,
    pub name: String,
    pub object_type: u16,
    pub tex1: i32,
    pub tex2: i32,
    pub skin: i32,
    pub level: i32,
    pub total_fame: i32,
    pub killer: String,
    pub world: String,
    /// Seconds before the server answered.
    pub ago: i64,
}

/// `/fame/recent`.
pub fn parse_recent_deaths(xml: &str) -> Result<Vec<RecentDeath>, String> {
    if let Some(e) = error_text(xml) {
        return Err(e);
    }
    let doc = roxmltree::Document::parse(xml).map_err(|e| e.to_string())?;
    let root = doc.root_element();
    Ok(root
        .children()
        .filter(|c| c.has_tag_name("Death"))
        .map(|e| {
            let int = |n: &str| child_text(e, n).and_then(parse_int).unwrap_or(0);
            RecentDeath {
                account_id: e.attribute("accountId").and_then(parse_int).unwrap_or(0) as i32,
                char_id: e.attribute("charId").and_then(parse_int).unwrap_or(0) as i32,
                name: child_text(e, "Name").unwrap_or("").to_owned(),
                object_type: int("ObjectType") as u16,
                tex1: int("Tex1") as i32,
                tex2: int("Tex2") as i32,
                skin: int("Texture") as i32,
                level: int("Level") as i32,
                total_fame: int("TotalFame") as i32,
                killer: child_text(e, "Killer").unwrap_or("").to_owned(),
                world: child_text(e, "World").unwrap_or("").to_owned(),
                ago: int("Ago"),
            }
        })
        .collect())
}

/// `/char/fame`: how a dead character's fame was counted.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct FameSummary {
    pub object_type: u16,
    pub level: i32,
    pub base_fame: i32,
    pub total_fame: i32,
    pub killed_by: String,
    /// (what, fame added).
    pub bonuses: Vec<(String, i32)>,
    /// (stat tag, value): shots, kills, dungeons and so on, as the server lists them.
    pub stats: Vec<(String, i64)>,
}

pub fn parse_char_fame(xml: &str) -> Result<FameSummary, String> {
    if let Some(e) = error_text(xml) {
        return Err(e);
    }
    let doc = roxmltree::Document::parse(xml).map_err(|e| e.to_string())?;
    let root = doc.root_element();
    let int = |n: &str| child_text(root, n).and_then(parse_int).unwrap_or(0);
    let mut out = FameSummary {
        base_fame: int("BaseFame") as i32,
        total_fame: int("TotalFame") as i32,
        killed_by: child_text(root, "KilledBy").unwrap_or("").to_owned(),
        ..Default::default()
    };
    if let Some(c) = root.children().find(|c| c.has_tag_name("Char")) {
        out.object_type = child_text(c, "ObjectType").and_then(parse_int).unwrap_or(0) as u16;
        out.level = child_text(c, "Level").and_then(parse_int).unwrap_or(0) as i32;
    }
    for e in root.children().filter(|c| c.is_element()) {
        let tag = e.tag_name().name();
        match tag {
            "Bonus" => out.bonuses.push((
                e.attribute("desc").or(e.attribute("id")).unwrap_or("").to_owned(),
                e.text().and_then(parse_int).unwrap_or(0) as i32,
            )),
            "Char" | "BaseFame" | "TotalFame" | "KilledBy" | "CreatedOn" => {}
            _ => {
                if let Some(v) = e.text().and_then(parse_int) {
                    out.stats.push((tag.to_owned(), v));
                }
            }
        }
    }
    Ok(out)
}

/// One member of `/guild/listMembers`.
#[derive(Debug, Clone, PartialEq)]
pub struct GuildMember {
    pub name: String,
    /// 0 initiate, 10 member, 20 officer, 30 leader, 40 founder.
    pub rank: i32,
    pub fame: i32,
    /// Unix time.
    pub last_seen: i64,
    /// Their living characters' fame (the Hall of Champions' Guild Living board), when they're
    /// ranked and have played in the last two weeks.
    pub alive: Option<i64>,
    /// They haven't played in the last two weeks, so their living fame doesn't count.
    pub away: bool,
}

/// `/guild/listMembers`: the guild, its hall and its members.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct Guild {
    pub name: String,
    pub members: Vec<GuildMember>,
    /// Its hall, 1-4 (0 from an older server).
    pub level: i32,
    /// Guild fame to spend, and all it has earned.
    pub fame: i64,
    pub total_fame: i64,
    pub vault_used: i32,
    pub vault_slots: i32,
    /// The next hall's price in guild fame (0 at the last).
    pub next_price: i64,
    /// What the hall gives every member ("+2% loot, +10% experience, ..."), and what the next
    /// would; empty from an older server, and the next at the last hall.
    pub buffs: String,
    pub next_buffs: String,
    /// Its place and fame on the Hall of Champions' Guild Living and Guild Dead boards.
    pub living: Option<(i32, i64)>,
    pub dead: Option<(i32, i64)>,
}

pub fn parse_guild(xml: &str) -> Result<Guild, String> {
    if let Some(e) = error_text(xml) {
        return Err(e);
    }
    let doc = roxmltree::Document::parse(xml).map_err(|e| e.to_string())?;
    let root = doc.root_element();
    let members = root
        .children()
        .filter(|c| c.has_tag_name("Member"))
        .map(|m| GuildMember {
            name: child_text(m, "Name").unwrap_or("").to_owned(),
            rank: child_text(m, "Rank").and_then(parse_int).unwrap_or(0) as i32,
            fame: child_text(m, "Fame").and_then(parse_int).unwrap_or(0) as i32,
            last_seen: child_text(m, "LastSeen").and_then(parse_int).unwrap_or(0),
            alive: child_text(m, "Alive").and_then(parse_int),
            away: child_text(m, "Away").is_some(),
        })
        .collect();
    let num = |tag: &str| child_text(root, tag).and_then(parse_int).unwrap_or(0);
    let board = |tag: &str| {
        root.children().find(|c| c.has_tag_name(tag)).map(|b| {
            let a = |n: &str| b.attribute(n).and_then(parse_int).unwrap_or(0);
            (a("rank") as i32, a("fame"))
        })
    };
    Ok(Guild {
        name: root.attribute("name").unwrap_or("").to_owned(),
        members,
        level: num("Level") as i32,
        fame: num("CurrentFame"),
        total_fame: num("TotalFame"),
        vault_used: num("VaultUsed") as i32,
        vault_slots: num("VaultSlots") as i32,
        next_price: num("NextPrice"),
        buffs: child_text(root, "Buffs").unwrap_or("").to_owned(),
        next_buffs: child_text(root, "NextBuffs").unwrap_or("").to_owned(),
        living: board("LivingBoard"),
        dead: board("DeadBoard"),
    })
}

/// `/account/grabPotions`: the stored count of each of the eight stat potions, and how many
/// of each the storage holds.
pub fn parse_potions(xml: &str) -> Result<(Vec<i32>, i32), String> {
    if let Some(e) = error_text(xml) {
        return Err(e);
    }
    let doc = roxmltree::Document::parse(xml).map_err(|e| e.to_string())?;
    let text = doc.root_element().text().unwrap_or("");
    let nums: Vec<i32> = text.split(',').map(|n| parse_int(n).unwrap_or(0) as i32).collect();
    let capacity = nums.get(8).copied().unwrap_or(50);
    Ok((nums.into_iter().take(8).collect(), capacity))
}

/// One run of `/arena/leaderboard`.
#[derive(Debug, Clone, PartialEq)]
pub struct ArenaRun {
    pub wave: i32,
    /// From the first wave to the end; negative for runs from before times were kept.
    pub seconds: i32,
    /// When it ended, unix ms.
    pub time: i64,
    /// "Arena" (the server reads older runs' "Battle Arena" and "Free Battle Arena" as it); empty for older runs.
    pub arena: String,
    pub players: Vec<String>,
}

/// `/arena/leaderboard`: the best arena runs, best first.
pub fn parse_arena_runs(xml: &str) -> Result<Vec<ArenaRun>, String> {
    if let Some(e) = error_text(xml) {
        return Err(e);
    }
    let doc = roxmltree::Document::parse(xml).map_err(|e| e.to_string())?;
    Ok(doc
        .root_element()
        .children()
        .filter(|c| c.has_tag_name("Run"))
        .map(|r| ArenaRun {
            wave: r.attribute("wave").and_then(parse_int).unwrap_or(0) as i32,
            seconds: r.attribute("seconds").and_then(parse_int).unwrap_or(-1) as i32,
            time: r.attribute("time").and_then(parse_int).unwrap_or(0),
            arena: r.attribute("arena").unwrap_or("").to_owned(),
            players: r.children().filter(|p| p.has_tag_name("Player")).filter_map(|p| p.text()).map(str::to_owned).collect(),
        })
        .collect())
}

/// `/champions/board`: the Hall of Champions, the living's leaderboard (the account server's
/// `ChampionsController`), ranked every few minutes.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct Champions {
    /// Not ranked yet (the account server has just started).
    pub pending: bool,
    /// The server's clock, unix seconds, and when it ranked last and will next.
    pub now: i64,
    pub updated: i64,
    pub next: i64,
    /// When the weekly boards start afresh.
    pub week_reset: i64,
    /// When the yearly boards start afresh.
    pub year_reset: i64,
    /// What every board's loot boosts add up to at most (0.04 is 4%).
    pub loot_cap: f64,
    pub boards: Vec<ChampionBoard>,
}

/// One board of the Hall of Champions.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct ChampionBoard {
    pub id: String,
    pub name: String,
    pub weekly: bool,
    /// It starts afresh on 1 January (Guild Dead).
    pub yearly: bool,
    /// What the value counts: "fame", "stars", "points", "wave", "dungeons".
    pub unit: String,
    /// How many are ranked on it.
    pub count: i32,
    pub description: String,
    /// The reward of each tier (#1, #2-3, #4-10): its places, its colour for a colour reward
    /// (else 0), and what it gives.
    pub rewards: Vec<(String, u32, String)>,
    /// The top 10, best first.
    pub entries: Vec<ChampionEntry>,
    /// The viewer's own place and value, when ranked (named in the request).
    pub me: Option<(i32, i64)>,
}

/// A champion: an account's place, and the character shown for it (its best living one;
/// `object_type` 0 when it has none). A group's place (Arena Group) has its `members`, each with
/// their own character, and their names joined as `name`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct ChampionEntry {
    pub rank: i32,
    pub name: String,
    pub value: i64,
    pub object_type: u16,
    pub skin: i32,
    pub tex1: i32,
    pub tex2: i32,
    pub level: i32,
    pub members: Vec<ChampionEntry>,
    /// A guild's place (Guilds): how many members it has, its name the guild's and its character
    /// its best member's; 0 for a player or a group.
    pub guild: i32,
}

impl ChampionEntry {
    /// The characters to show: the group's members, else this one.
    pub fn faces(&self) -> &[ChampionEntry] {
        if self.members.is_empty() { std::slice::from_ref(self) } else { &self.members }
    }

    /// Whether this place is `name`'s (theirs alone, or their group's).
    pub fn has(&self, name: &str) -> bool {
        !name.is_empty() && self.faces().iter().any(|f| f.name.eq_ignore_ascii_case(name))
    }
}

/// `/champions/board`.
pub fn parse_champions(xml: &str) -> Result<Champions, String> {
    if let Some(e) = error_text(xml) {
        return Err(e);
    }
    let doc = roxmltree::Document::parse(xml).map_err(|e| e.to_string())?;
    let root = doc.root_element();
    let int = |n: roxmltree::Node, a: &str| n.attribute(a).and_then(parse_int).unwrap_or(0);
    let look = |e: roxmltree::Node, rank: i32, value: i64| ChampionEntry {
        rank,
        name: e.attribute("name").unwrap_or("").to_owned(),
        value,
        object_type: int(e, "objectType") as u16,
        skin: int(e, "skin") as i32,
        tex1: int(e, "tex1") as i32,
        tex2: int(e, "tex2") as i32,
        level: int(e, "level") as i32,
        members: Vec::new(),
        guild: int(e, "guild") as i32,
    };
    Ok(Champions {
        pending: int(root, "pending") != 0,
        now: int(root, "now"),
        updated: int(root, "updated"),
        next: int(root, "next"),
        week_reset: int(root, "weekReset"),
        year_reset: int(root, "yearReset"),
        loot_cap: root.attribute("lootCap").and_then(|v| v.parse().ok()).unwrap_or(0.0),
        boards: root
            .children()
            .filter(|c| c.has_tag_name("Board"))
            .map(|b| ChampionBoard {
                id: b.attribute("id").unwrap_or("").to_owned(),
                name: b.attribute("name").unwrap_or("").to_owned(),
                weekly: int(b, "weekly") != 0,
                yearly: int(b, "yearly") != 0,
                unit: b.attribute("unit").unwrap_or("").to_owned(),
                count: int(b, "count") as i32,
                description: b.children().find(|c| c.has_tag_name("Description")).and_then(|d| d.text()).unwrap_or("").to_owned(),
                rewards: b
                    .children()
                    .filter(|c| c.has_tag_name("Reward"))
                    .map(|r| (r.attribute("places").unwrap_or("").to_owned(), int(r, "color") as u32, r.text().unwrap_or("").to_owned()))
                    .collect(),
                entries: b
                    .children()
                    .filter(|c| c.has_tag_name("Entry"))
                    .map(|e| ChampionEntry {
                        members: e.children().filter(|m| m.has_tag_name("Member")).map(|m| look(m, 0, 0)).collect(),
                        ..look(e, int(e, "rank") as i32, int(e, "value"))
                    })
                    .collect(),
                me: b.children().find(|c| c.has_tag_name("Me")).map(|m| (int(m, "rank") as i32, int(m, "value"))),
            })
            .collect(),
    })
}

/// A plain-text answer (the guild board), or the `<Error>`'s text.
pub fn text_answer(body: &str) -> Result<String, String> {
    match error_text(body) {
        Some(e) => Err(e),
        None => Ok(body.to_owned()),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn char_list() {
        let xml = r#"<Chars nextCharId="5" maxNumChars="2">
            <Char id="3"><ObjectType>782</ObjectType><Level>20</Level><Dead>False</Dead></Char>
            <Char id="4"><ObjectType>0x0300</ObjectType><Level>1</Level></Char>
            <Servers><Server><Name>Localhost</Name><DNS>127.0.0.1</DNS><Port>2053</Port>
                <Usage>0.5</Usage><AdminOnly>false</AdminOnly><UsageText>3/6</UsageText></Server></Servers>
        </Chars>"#;
        let list = parse_char_list(xml).unwrap();
        assert_eq!((list.next_char_id, list.max_num_chars), (5, 2));
        assert_eq!((list.chars[0].id, list.chars[0].object_type, list.chars[0].level, list.chars[0].dead), (3, 782, 20, false));
        assert_eq!(list.chars[1].object_type, 0x300);
        assert_eq!(list.servers[0], ServerEntry { name: "Localhost".into(), host: "127.0.0.1".into(), port: 2053, admin_only: false, usage: 0.5, usage_text: "3/6".into() });
    }

    #[test]
    fn account_and_equipment() {
        let xml = r#"<Chars nextCharId="2" maxNumChars="2">
            <Char id="1"><ObjectType>782</ObjectType><Equipment>2580, -1, 0x0A14</Equipment>
                <CurrentFame>12</CurrentFame><Tex1>16777216</Tex1></Char>
            <Account><Name>Tester</Name><Credits>50</Credits>
                <Stats><ClassStats objectType="0x030e"><BestLevel>7</BestLevel><BestFame>3</BestFame></ClassStats>
                <Fame>99</Fame></Stats></Account>
        </Chars>"#;
        let list = parse_char_list(xml).unwrap();
        assert_eq!(list.chars[0].equipment, vec![2580, -1, 0xA14]);
        assert_eq!((list.chars[0].fame, list.chars[0].tex1), (12, 16777216));
        assert_eq!((list.name.as_str(), list.credits, list.fame), ("Tester", 50, 99));
        assert_eq!(list.class_stats[&0x30e], (7, 3));
        assert_eq!(success("<Success/>"), Ok(()));
        assert_eq!(success("<Error>Duplicated name</Error>"), Err("Duplicated name".into()));
    }

    #[test]
    fn slots_skins_legends_and_fame() {
        let xml = r#"<Chars nextCharId="2" maxNumChars="1"><OwnedSkins>835,0x0343</OwnedSkins>
            <Account><AccountId>7</AccountId><NextCharSlotPrice>500</NextCharSlotPrice>
            <NextCharSlotCurrency>1</NextCharSlotCurrency></Account></Chars>"#;
        let list = parse_char_list(xml).unwrap();
        assert_eq!((list.account_id, list.next_slot_price, list.next_slot_currency), (7, 500, 1));
        assert_eq!(list.owned_skins, vec![835, 0x343]);

        let legends = parse_fame_list(
            r#"<FameList timespan="week"><FameListElem accountId="1" charId="4"><Name>Bob</Name>
            <ObjectType>782</ObjectType><Equipment>2580, -1, -12286, 65535</Equipment><TotalFame>120</TotalFame>
            </FameListElem></FameList>"#,
        )
        .unwrap();
        assert_eq!((legends[0].name.as_str(), legends[0].char_id, legends[0].total_fame), ("Bob", 4, 120));
        // The server writes the legends' items as signed numbers: -12286 is 0xD002.
        assert_eq!(legends[0].equipment, vec![2580, -1, 0xD002, -1]);

        let fame = parse_char_fame(
            r#"<Fame><Char id="4"><ObjectType>782</ObjectType><Level>20</Level></Char><BaseFame>100</BaseFame>
            <TotalFame>130</TotalFame><Shots>50</Shots><Bonus id="Ancestor" desc="First born">30</Bonus>
            <CreatedOn>1</CreatedOn><KilledBy>Pirate</KilledBy></Fame>"#,
        )
        .unwrap();
        assert_eq!((fame.object_type, fame.level, fame.base_fame, fame.total_fame), (782, 20, 100, 130));
        assert_eq!(fame.bonuses, vec![("First born".to_owned(), 30)]);
        assert_eq!(fame.stats, vec![("Shots".to_owned(), 50)]);
        assert_eq!(fame.killed_by, "Pirate");
    }

    #[test]
    fn guild_and_potions() {
        let g = parse_guild(
            r#"<Guild id="3" name="Knights"><TotalFame>9</TotalFame><Member><Name>Bob</Name><Rank>40</Rank>
            <Fame>12</Fame><LastSeen>100</LastSeen></Member></Guild>"#,
        )
        .unwrap();
        assert_eq!(g.name, "Knights");
        assert_eq!(g.members[0], GuildMember { name: "Bob".into(), rank: 40, fame: 12, last_seen: 100, alive: None, away: false });
        assert_eq!((g.total_fame, g.level, g.living, g.dead), (9, 0, None, None));
        assert_eq!((g.buffs.as_str(), g.next_buffs.as_str()), ("", ""));
        let g = parse_guild(
            r#"<Guild id="3" name="Knights"><TotalFame>900</TotalFame><CurrentFame>40</CurrentFame><Level>2</Level>
            <VaultSlots>32</VaultSlots><VaultUsed>5</VaultUsed><NextPrice>30000</NextPrice><Buffs>+1.5% loot</Buffs><NextBuffs>+2% loot</NextBuffs><LivingBoard rank="1" fame="1500"/><DeadBoard rank="3" fame="900"/>
            <Member><Name>Bob</Name><Rank>40</Rank><Fame>12</Fame><LastSeen>100</LastSeen><Alive>600</Alive></Member></Guild>"#,
        )
        .unwrap();
        assert_eq!((g.level, g.fame, g.vault_used, g.vault_slots, g.next_price), (2, 40, 5, 32, 30000));
        assert_eq!((g.living, g.dead), (Some((1, 1500)), Some((3, 900))));
        assert_eq!((g.buffs.as_str(), g.next_buffs.as_str()), ("+1.5% loot", "+2% loot"));
        assert_eq!(g.members[0].alive, Some(600));
        let (counts, cap) = parse_potions("<StoredPotions>1,2,3,4,5,6,7,8,100</StoredPotions>").unwrap();
        assert_eq!((counts, cap), (vec![1, 2, 3, 4, 5, 6, 7, 8], 100));
        assert_eq!(text_answer("Hello guild"), Ok("Hello guild".into()));
    }

    #[test]
    fn champions() {
        let xml = r##"<Champions now="100" updated="90" next="390" weekReset="5000" lootCap="0.04">
            <Board id="fame" name="Fame" weekly="0" unit="fame" count="12"><Description>Most fame.</Description>
                <Reward places="#1" color="0">+3% loot</Reward><Reward places="#2-3" color="0">+2% loot</Reward>
                <Entry rank="1" name="Meapy" value="1234" objectType="782" skin="0" tex1="5" tex2="0" level="20"/>
                <Me rank="11" value="40"/></Board>
            <Board id="stars" name="Stars" weekly="0" unit="stars" count="0"><Description/>
                <Reward places="#1" color="16765514">Gold glow</Reward></Board>
            <Board id="arenaGroup" name="Arena Group" weekly="0" unit="wave" count="1"><Description/>
                <Entry rank="1" value="31" name="Ann, Bo" objectType="782" skin="0" tex1="0" tex2="0" level="20">
                    <Member name="Ann" objectType="782" skin="0" tex1="0" tex2="0" level="20"/>
                    <Member name="Bo" objectType="775" skin="0" tex1="0" tex2="0" level="12"/></Entry></Board></Champions>"##;
        let c = parse_champions(xml).unwrap();
        assert!(!c.pending);
        assert_eq!((c.now, c.week_reset, c.loot_cap), (100, 5000, 0.04));
        assert_eq!(c.boards.len(), 3);
        let group = &c.boards[2].entries[0];
        assert_eq!((group.rank, group.value, group.members.len()), (1, 31, 2));
        assert_eq!((group.members[1].name.as_str(), group.members[1].object_type, group.members[1].level), ("Bo", 775, 12));
        assert!(group.has("bo") && !group.has("Cy") && group.faces().len() == 2);
        assert_eq!(c.boards[0].entries[0].faces().len(), 1);
        let fame = &c.boards[0];
        assert_eq!((fame.id.as_str(), fame.count, fame.me), ("fame", 12, Some((11, 40))));
        assert_eq!(fame.entries[0].name, "Meapy");
        assert_eq!((fame.entries[0].object_type, fame.entries[0].tex1, fame.entries[0].level), (782, 5, 20));
        assert_eq!(fame.rewards[1], ("#2-3".to_owned(), 0, "+2% loot".to_owned()));
        assert_eq!(c.boards[1].rewards[0].1, 0xFFD24A);
        assert!(c.boards[1].entries.is_empty() && c.boards[1].me.is_none());
        assert!(parse_champions(r#"<Champions pending="1" now="5"/>"#).unwrap().pending);
    }

    #[test]
    fn arena_runs() {
        let runs = parse_arena_runs(
            r#"<ArenaRuns><Run wave="23" seconds="754" time="1760000000000" arena="Battle Arena"><Player>Ann</Player><Player>Bo</Player></Run>
            <Run wave="9" seconds="-1" time="1" arena=""><Player>Cy</Player></Run></ArenaRuns>"#,
        )
        .unwrap();
        assert_eq!(runs.len(), 2);
        assert_eq!((runs[0].wave, runs[0].seconds, runs[0].arena.as_str()), (23, 754, "Battle Arena"));
        assert_eq!(runs[0].players, vec!["Ann".to_owned(), "Bo".to_owned()]);
        assert_eq!((runs[1].seconds, runs[1].players.len()), (-1, 1));
    }

    #[test]
    fn graveyard_news() {
        let xml = r#"<Chars nextCharId="3" maxNumChars="2"><News><Item><Icon>fame</Icon><Title>Your Wizard died at level 5</Title>
            <TagLine>You earned 12 glorious Fame</TagLine><Link>fame:7</Link><Date>1700000000</Date></Item>
            <Item><Icon>info</Icon><Title>Patch</Title><TagLine/><Link>https://x</Link><Date>1</Date></Item></News></Chars>"#;
        let list = parse_char_list(xml).unwrap();
        assert_eq!(list.news.len(), 2);
        assert_eq!(list.news[0].death_char_id(), Some(7));
        assert_eq!(list.news[0].date, 1_700_000_000);
        assert_eq!(list.news[1].death_char_id(), None);
    }

    #[test]
    fn recent_deaths() {
        let xml = r#"<RecentDeaths><Death accountId="1" charId="9"><Name>Tester</Name><ObjectType>782</ObjectType>
            <Texture>0</Texture><Tex1>0</Tex1><Tex2>0</Tex2><Level>12</Level><TotalFame>40</TotalFame>
            <Killer>Pirate</Killer><World>Realm</World><Time>1700000000</Time><Ago>95</Ago></Death>
            <Death accountId="2" charId="3"><Name>B</Name><Killer></Killer></Death></RecentDeaths>"#;
        let d = parse_recent_deaths(xml).unwrap();
        assert_eq!(d.len(), 2);
        assert_eq!((d[0].account_id, d[0].char_id, d[0].object_type, d[0].level, d[0].total_fame), (1, 9, 782, 12, 40));
        assert_eq!((d[0].killer.as_str(), d[0].world.as_str(), d[0].ago), ("Pirate", "Realm", 95));
        assert_eq!((d[1].name.as_str(), d[1].killer.as_str()), ("B", ""));
    }

    #[test]
    fn error_answer() {
        assert_eq!(parse_char_list("<Error>Account credentials not valid</Error>"), Err("Account credentials not valid".into()));
    }
}
