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
    pub total_fame: i32,
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
}

/// `/guild/listMembers`: the guild's name and members.
pub fn parse_guild(xml: &str) -> Result<(String, Vec<GuildMember>), String> {
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
        })
        .collect();
    Ok((root.attribute("name").unwrap_or("").to_owned(), members))
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
    /// "Arena", "Battle Arena" or "Free Battle Arena"; empty for older runs.
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
        let (name, members) = parse_guild(
            r#"<Guild id="3" name="Knights"><TotalFame>9</TotalFame><Member><Name>Bob</Name><Rank>40</Rank>
            <Fame>12</Fame><LastSeen>100</LastSeen></Member></Guild>"#,
        )
        .unwrap();
        assert_eq!(name, "Knights");
        assert_eq!(members[0], GuildMember { name: "Bob".into(), rank: 40, fame: 12, last_seen: 100 });
        let (counts, cap) = parse_potions("<StoredPotions>1,2,3,4,5,6,7,8,100</StoredPotions>").unwrap();
        assert_eq!((counts, cap), (vec![1, 2, 3, 4, 5, 6, 7, 8], 100));
        assert_eq!(text_answer("Hello guild"), Ok("Hello guild".into()));
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
