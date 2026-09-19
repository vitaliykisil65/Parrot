using Parrot.Core.Models;

namespace Parrot.Core.Data;

/// <summary>
/// Gives a brand-new install something to show on the very first prompt. Without this the
/// app would start silent and look broken.
/// </summary>
public static class SeedData
{
    public const string StarterDeckName = "Англійська — старт";

    public static void EnsureSeeded(CardRepository repository)
    {
        if (repository.GetDecks().Count > 0)
            return;

        var deckId = repository.AddDeck(new Deck
        {
            Name = StarterDeckName,
            FrontLang = "en",
            BackLang = "uk",
        });

        CsvCardIo.Parse(StarterDeckCsv, deckId, out var cards);

        foreach (var card in cards)
            repository.AddCard(card);
    }

    private const string StarterDeckCsv = """
        Front,Back,Hint,Example,Tags
        to figure out,зрозуміти; розібратися,phrasal verb,I can't figure out what he wants.,phrasal
        to give up,здатися; кинути,phrasal verb,Don't give up so easily.,phrasal
        to look forward to,з нетерпінням чекати,phrasal verb,I'm looking forward to the weekend.,phrasal
        to come up with,придумати; вигадати,phrasal verb,She came up with a great idea.,phrasal
        to run out of,вичерпати; закінчитися,phrasal verb,We ran out of coffee.,phrasal
        to put off,відкладати,phrasal verb,Stop putting off the decision.,phrasal
        to take over,перебрати; захопити,phrasal verb,A new manager took over the team.,phrasal
        to point out,вказати; зазначити,phrasal verb,He pointed out the mistake.,phrasal
        to carry out,виконувати; здійснювати,phrasal verb,They carried out the plan.,phrasal
        to bring up,порушити тему; виховувати,phrasal verb,Don't bring that up again.,phrasal
        to get rid of,позбутися,phrasal verb,I need to get rid of these old files.,phrasal
        to keep up with,встигати за; не відставати,phrasal verb,It's hard to keep up with the news.,phrasal
        to look into,розглянути; вивчити,phrasal verb,We'll look into the problem.,phrasal
        to turn down,відхилити; відмовити,phrasal verb,She turned down the offer.,phrasal
        to work out,спрацювати; тренуватися,phrasal verb,Everything worked out fine.,phrasal
        meanwhile,тим часом,,Meanwhile I'll prepare the report.,linking
        however,проте; однак,,However it is not that simple.,linking
        therefore,отже; тому,,Therefore we need another approach.,linking
        nevertheless,проте; все ж таки,,Nevertheless he agreed.,linking
        besides,крім того,,Besides it's cheaper.,linking
        regardless,незалежно від; байдуже,,He went regardless of the weather.,linking
        whereas,тоді як,,He likes tea whereas she prefers coffee.,linking
        accordingly,відповідно,,Plan accordingly.,linking
        eventually,врешті-решт,,Eventually she agreed.,linking
        arguably,можливо; імовірно,,This is arguably the best option.,linking
        to be worth it,бути вартим того,,The trip was worth it.,idiom
        to make sense,мати сенс,,That doesn't make sense.,idiom
        to keep in mind,мати на увазі,,Keep in mind that it's expensive.,idiom
        to take into account,брати до уваги,,Take the delay into account.,idiom
        on purpose,навмисно,,He did it on purpose.,idiom
        by the way,до речі,,By the way I've finished it.,idiom
        in a nutshell,коротко кажучи,,In a nutshell we failed.,idiom
        at first glance,на перший погляд,,At first glance it looks simple.,idiom
        for the time being,поки що; наразі,,For the time being we'll wait.,idiom
        sooner or later,рано чи пізно,,Sooner or later he'll find out.,idiom
        once in a while,час від часу,,I visit them once in a while.,idiom
        to get the hang of,набити руку; призвичаїтися,,You'll get the hang of it.,idiom
        to be on the same page,розуміти одне одного,,Let's make sure we're on the same page.,idiom
        to cut corners,робити абияк; економити на якості,,Don't cut corners on testing.,idiom
        to bite the bullet,зціпити зуби; наважитися,,I bit the bullet and told her.,idiom
        accurate,точний,,The estimate was accurate.,adjective
        reliable,надійний,,A reliable colleague.,adjective
        aware,обізнаний; свідомий,,Are you aware of the risk?,adjective
        willing,готовий; охочий,,I'm willing to help.,adjective
        reasonable,розумний; прийнятний,,A reasonable price.,adjective
        tough,важкий; жорсткий,,A tough decision.,adjective
        thorough,ретельний,,A thorough review.,adjective
        straightforward,простий; прямолінійний,,A straightforward answer.,adjective
        redundant,зайвий; надлишковий,,This check is redundant.,adjective
        feasible,здійсненний,,Is that feasible by Friday?,adjective
        cumbersome,громіздкий; незручний,,A cumbersome process.,adjective
        blunt,прямий; різкий,,To be blunt I disagree.,adjective
        subtle,ледь помітний; тонкий,,A subtle difference.,adjective
        stubborn,упертий,,He is stubborn about it.,adjective
        eager,палкий; нетерплячий,,She was eager to start.,adjective
        to assume,припускати,,I assumed you knew.,verb
        to acknowledge,визнавати,,He acknowledged the error.,verb
        to postpone,відкласти,,We postponed the release.,verb
        to accomplish,досягти; виконати,,We accomplished the goal.,verb
        to maintain,підтримувати; обслуговувати,,Who maintains this code?,verb
        to enhance,покращити; підсилити,,This enhances performance.,verb
        to estimate,оцінювати,,Can you estimate the effort?,verb
        to enforce,примушувати; забезпечувати виконання,,The rule is not enforced.,verb
        to mitigate,пом'якшити; знизити ризик,,We must mitigate the risk.,verb
        to resemble,нагадувати; бути схожим,,It resembles the old version.,verb
        to struggle,мати труднощі; боротися,,I'm struggling with this bug.,verb
        to encourage,заохочувати,,She encouraged me to apply.,verb
        to reveal,виявити; розкрити,,The test revealed a leak.,verb
        to pursue,переслідувати; домагатися,,He pursued the idea.,verb
        to withstand,витримувати,,It can withstand heavy load.,verb
        drawback,недолік,,The main drawback is the price.,noun
        trade-off,компроміс; вибір між,,There's a trade-off between speed and cost.,noun
        insight,розуміння; прозріння,,That gave me a useful insight.,noun
        constraint,обмеження,,Time is the main constraint.,noun
        bottleneck,вузьке місце,,The database is the bottleneck.,noun
        assumption,припущення,,That assumption was wrong.,noun
        outcome,результат; наслідок,,The outcome was surprising.,noun
        approach,підхід,,A different approach is needed.,noun
        concern,занепокоєння; проблема,,I have one concern.,noun
        scope,обсяг; межі,,That's out of scope.,noun
        leverage,важіль; перевага,,We have little leverage here.,noun
        workaround,обхідний шлях,,It's only a workaround.,noun
        milestone,віха; етап,,We hit the first milestone.,noun
        shortcoming,вада; недолік,,The main shortcoming is speed.,noun
        deadline,кінцевий термін,,The deadline is Friday.,noun
        """;
}
