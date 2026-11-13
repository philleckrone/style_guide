using System;

namespace SendNotes
{
    /// <summary>
    /// The window's text in English, Spanish or French, picked from Paratext's interface language.
    /// Keep this file ASCII: run tools\asciify_cs.py after typing accented letters.
    /// </summary>
    public sealed class Strings
    {
        /// <summary>The tool's version: shown in the window title and reported to Paratext.</summary>
        public const string ToolVersion = "1.3";

        public string MenuText;
        public string Title;
        public string Description;
        public string Intro;
        public string FromLabel;
        public string ToLabel;
        public string ChooseTarget;
        public string NoTargets;
        public string BookLabel;
        public string ChapterLabel;
        public string OnlyVerse;
        public string PrevChapter;
        public string NextChapter;
        public string Follow;
        public string HideSent;
        public string IncludeResolved;
        public string NotConsultant;
        public string ColSend;
        public string ColRef;
        public string ColText;
        public string ColComments;
        public string ColSent;
        public string Resolved;
        public string NoText;
        public string CommentsLabel;
        public string CommentLine;
        public string SentLine;
        public string PreviewLabel;
        public string AttachedTo;
        public string GoToButton;
        public string TickAll;
        public string UntickAll;
        public string SendButton;
        public string Close;
        public string Reading;
        public string Counts;
        public string NoNotes;
        public string HiddenSent;
        public string NothingTicked;
        public string NoTarget;
        public string ConfirmTitle;
        public string Confirm;
        public string ConfirmResend;
        /// <summary>The comment added to a sent note: {0} = target project, {1} = date (yyyy-MM-dd).</summary>
        public string SentMarker;
        public string ResultsLabel;
        public string ResSent;
        public string ResSentLine;
        public string ResFailed;
        public string ResNoTargetLock;
        public string ResNoSourceLock;
        public string ResMarkFailed;
        public string OpenProjectFirst;
        public string Error;

        public static Strings For(string locale)
        {
            string lang = string.IsNullOrEmpty(locale) ? "" : locale.ToLowerInvariant();
            if (lang.StartsWith("es", StringComparison.Ordinal)) return Spanish();
            if (lang.StartsWith("fr", StringComparison.Ordinal)) return French();
            return English();
        }

        public static Strings English()
        {
            var s = new Strings();
            s.MenuText = "Send Notes to Project...";
            s.Title = "Send Notes to Project";
            s.Description = "Sends notes written in a consultant notes project to another project: you tick the notes of a chapter, choose which comment of each note to send, and each one becomes a new note at the same verse in the project you choose.";
            s.Intro = "Tick the notes to send. For each note, click the comment to send in the list below (the last one is chosen at first). " +
                "Each comment sent becomes a new note at the same verse in the receiving project, and \"Sent to ...\" is added to the note here.";
            s.FromLabel = "From:";
            s.ToLabel = "Send to:";
            s.ChooseTarget = "(choose a project)";
            s.NoTargets = "There is no other project you may add notes to.";
            s.BookLabel = "Book:";
            s.ChapterLabel = "Chapter:";
            s.OnlyVerse = "Only verse:";
            s.PrevChapter = "< Previous";
            s.NextChapter = "Next >";
            s.Follow = "Follow Paratext";
            s.HideSent = "Hide notes already sent to this project";
            s.IncludeResolved = "Include resolved notes";
            s.NotConsultant = "{0} is not a consultant notes project.";
            s.ColSend = "Send";
            s.ColRef = "Reference";
            s.ColText = "Comment to send";
            s.ColComments = "Comments";
            s.ColSent = "Already sent";
            s.Resolved = "(resolved)";
            s.NoText = "(no text to send)";
            s.CommentsLabel = "Comments of this note (click the one to send):";
            s.CommentLine = "{0}. {1}, {2}: {3}";
            s.SentLine = "    [{0}]";
            s.PreviewLabel = "Will be sent:";
            s.AttachedTo = "Attached to the words: \"{0}\"";
            s.GoToButton = "Go to in Paratext";
            s.TickAll = "Tick all";
            s.UntickAll = "Untick all";
            s.SendButton = "Send ticked notes ({0})";
            s.Close = "Close";
            s.Reading = "Reading {0} {1}...";
            s.Counts = "{0} {1}: {2} note(s) shown, {3} ticked.";
            s.NoNotes = "There are no notes to show here.";
            s.HiddenSent = "{0} note(s) already sent are hidden.";
            s.NothingTicked = "Tick at least one note to send.";
            s.NoTarget = "Choose the project to send the notes to.";
            s.ConfirmTitle = "Send notes";
            s.Confirm = "Send {0} note(s) to {1}?";
            s.ConfirmResend = "{0} of them were already sent to {1}. They will be sent again.";
            s.SentMarker = "Sent to {0} on {1}.";
            s.ResultsLabel = "Results:";
            s.ResSent = "{0} note(s) sent to {1}:";
            s.ResSentLine = "  {0} -> {1} {2}";
            s.ResFailed = "Not sent: {0}: {1}";
            s.ResNoTargetLock = "You cannot add notes to {0} now (you may be an observer there, or its notes are in use). Nothing was sent.";
            s.ResNoSourceLock = "The notes were sent, but \"Sent to\" could not be added in {0} (you may be an observer there, or its notes are in use).";
            s.ResMarkFailed = "\"Sent to\" not added to {0}: {1}";
            s.OpenProjectFirst = "There is no consultant notes project to send notes from. Open the project that has the notes, then choose this command again.";
            s.Error = "Send Notes to Project stopped because of an error:\n{0}";
            return s;
        }

        public static Strings Spanish()
        {
            var s = new Strings();
            s.MenuText = "Enviar notas a un proyecto...";
            s.Title = "Enviar notas a un proyecto";
            s.Description = "Env\u00EDa notas escritas en un proyecto de notas de consultor a otro proyecto: usted marca las notas de un cap\u00EDtulo, elige qu\u00E9 comentario de cada nota enviar, y cada uno se convierte en una nota nueva en el mismo vers\u00EDculo del proyecto que usted elija.";
            s.Intro = "Marque las notas que quiere enviar. Para cada nota, haga clic en la lista de abajo en el comentario que quiere enviar (al principio se elige el \u00FAltimo). " +
                "Cada comentario enviado se convierte en una nota nueva en el mismo vers\u00EDculo del proyecto que la recibe, y se agrega \"Enviada a ...\" a la nota de aqu\u00ED.";
            s.FromLabel = "De:";
            s.ToLabel = "Enviar a:";
            s.ChooseTarget = "(elija un proyecto)";
            s.NoTargets = "No hay otro proyecto al que usted pueda agregar notas.";
            s.BookLabel = "Libro:";
            s.ChapterLabel = "Cap\u00EDtulo:";
            s.OnlyVerse = "Solo el vers\u00EDculo:";
            s.PrevChapter = "< Anterior";
            s.NextChapter = "Siguiente >";
            s.Follow = "Seguir a Paratext";
            s.HideSent = "Ocultar las notas ya enviadas a este proyecto";
            s.IncludeResolved = "Incluir las notas resueltas";
            s.NotConsultant = "{0} no es un proyecto de notas de consultor.";
            s.ColSend = "Enviar";
            s.ColRef = "Referencia";
            s.ColText = "Comentario que se enviar\u00E1";
            s.ColComments = "Comentarios";
            s.ColSent = "Ya enviada";
            s.Resolved = "(resuelta)";
            s.NoText = "(no hay texto que enviar)";
            s.CommentsLabel = "Comentarios de esta nota (haga clic en el que quiere enviar):";
            s.CommentLine = "{0}. {1}, {2}: {3}";
            s.SentLine = "    [{0}]";
            s.PreviewLabel = "Se enviar\u00E1:";
            s.AttachedTo = "Vinculada a las palabras: \"{0}\"";
            s.GoToButton = "Ir al vers\u00EDculo en Paratext";
            s.TickAll = "Marcar todas";
            s.UntickAll = "Desmarcar todas";
            s.SendButton = "Enviar las notas marcadas ({0})";
            s.Close = "Cerrar";
            s.Reading = "Leyendo {0} {1}...";
            s.Counts = "{0} {1}: {2} nota(s) mostrada(s), {3} marcada(s).";
            s.NoNotes = "No hay notas que mostrar aqu\u00ED.";
            s.HiddenSent = "Se ocultan {0} nota(s) ya enviada(s).";
            s.NothingTicked = "Marque al menos una nota para enviar.";
            s.NoTarget = "Elija el proyecto al que quiere enviar las notas.";
            s.ConfirmTitle = "Enviar notas";
            s.Confirm = "\u00BFEnviar {0} nota(s) a {1}?";
            s.ConfirmResend = "{0} de ellas ya se enviaron a {1}. Se enviar\u00E1n de nuevo.";
            s.SentMarker = "Enviada a {0} el {1}.";
            s.ResultsLabel = "Resultados:";
            s.ResSent = "{0} nota(s) enviada(s) a {1}:";
            s.ResSentLine = "  {0} -> {1} {2}";
            s.ResFailed = "No enviada: {0}: {1}";
            s.ResNoTargetLock = "Ahora usted no puede agregar notas a {0} (quiz\u00E1s es observador all\u00ED, o sus notas est\u00E1n en uso). No se envi\u00F3 nada.";
            s.ResNoSourceLock = "Las notas se enviaron, pero no se pudo agregar \"Enviada a\" en {0} (quiz\u00E1s es observador all\u00ED, o sus notas est\u00E1n en uso).";
            s.ResMarkFailed = "No se agreg\u00F3 \"Enviada a\" a {0}: {1}";
            s.OpenProjectFirst = "No hay ning\u00FAn proyecto de notas de consultor desde el cual enviar notas. Abra el proyecto que tiene las notas y elija este comando de nuevo.";
            s.Error = "Enviar notas a un proyecto se detuvo por un error:\n{0}";
            return s;
        }

        public static Strings French()
        {
            var s = new Strings();
            s.MenuText = "Envoyer des notes \u00E0 un projet...";
            s.Title = "Envoyer des notes \u00E0 un projet";
            s.Description = "Envoie des notes \u00E9crites dans un projet de notes de consultant vers un autre projet : vous cochez les notes d'un chapitre, choisissez le commentaire de chaque note \u00E0 envoyer, et chacun devient une nouvelle note au m\u00EAme verset dans le projet que vous choisissez.";
            s.Intro = "Cochez les notes \u00E0 envoyer. Pour chaque note, cliquez dans la liste ci-dessous sur le commentaire \u00E0 envoyer (le dernier est choisi au d\u00E9part). " +
                "Chaque commentaire envoy\u00E9 devient une nouvelle note au m\u00EAme verset dans le projet qui le re\u00E7oit, et \u00AB Envoy\u00E9e \u00E0 ... \u00BB est ajout\u00E9 \u00E0 la note d'ici.";
            s.FromLabel = "De :";
            s.ToLabel = "Envoyer \u00E0 :";
            s.ChooseTarget = "(choisissez un projet)";
            s.NoTargets = "Il n'y a aucun autre projet auquel vous pouvez ajouter des notes.";
            s.BookLabel = "Livre :";
            s.ChapterLabel = "Chapitre :";
            s.OnlyVerse = "Seulement le verset :";
            s.PrevChapter = "< Pr\u00E9c\u00E9dent";
            s.NextChapter = "Suivant >";
            s.Follow = "Suivre Paratext";
            s.HideSent = "Masquer les notes d\u00E9j\u00E0 envoy\u00E9es \u00E0 ce projet";
            s.IncludeResolved = "Inclure les notes r\u00E9solues";
            s.NotConsultant = "{0} n'est pas un projet de notes de consultant.";
            s.ColSend = "Envoyer";
            s.ColRef = "R\u00E9f\u00E9rence";
            s.ColText = "Commentaire \u00E0 envoyer";
            s.ColComments = "Commentaires";
            s.ColSent = "D\u00E9j\u00E0 envoy\u00E9e";
            s.Resolved = "(r\u00E9solue)";
            s.NoText = "(aucun texte \u00E0 envoyer)";
            s.CommentsLabel = "Commentaires de cette note (cliquez sur celui \u00E0 envoyer) :";
            s.CommentLine = "{0}. {1}, {2} : {3}";
            s.SentLine = "    [{0}]";
            s.PreviewLabel = "Sera envoy\u00E9 :";
            s.AttachedTo = "Attach\u00E9e aux mots : \u00AB {0} \u00BB";
            s.GoToButton = "Aller au verset dans Paratext";
            s.TickAll = "Tout cocher";
            s.UntickAll = "Tout d\u00E9cocher";
            s.SendButton = "Envoyer les notes coch\u00E9es ({0})";
            s.Close = "Fermer";
            s.Reading = "Lecture de {0} {1}...";
            s.Counts = "{0} {1} : {2} note(s) affich\u00E9e(s), {3} coch\u00E9e(s).";
            s.NoNotes = "Il n'y a aucune note \u00E0 afficher ici.";
            s.HiddenSent = "{0} note(s) d\u00E9j\u00E0 envoy\u00E9e(s) sont masqu\u00E9es.";
            s.NothingTicked = "Cochez au moins une note \u00E0 envoyer.";
            s.NoTarget = "Choisissez le projet auquel envoyer les notes.";
            s.ConfirmTitle = "Envoyer des notes";
            s.Confirm = "Envoyer {0} note(s) \u00E0 {1} ?";
            s.ConfirmResend = "{0} d'entre elles ont d\u00E9j\u00E0 \u00E9t\u00E9 envoy\u00E9es \u00E0 {1}. Elles seront envoy\u00E9es de nouveau.";
            s.SentMarker = "Envoy\u00E9e \u00E0 {0} le {1}.";
            s.ResultsLabel = "R\u00E9sultats :";
            s.ResSent = "{0} note(s) envoy\u00E9e(s) \u00E0 {1} :";
            s.ResSentLine = "  {0} -> {1} {2}";
            s.ResFailed = "Non envoy\u00E9e : {0} : {1}";
            s.ResNoTargetLock = "Vous ne pouvez pas ajouter de notes \u00E0 {0} maintenant (vous y \u00EAtes peut-\u00EAtre observateur, ou ses notes sont en cours d'utilisation). Rien n'a \u00E9t\u00E9 envoy\u00E9.";
            s.ResNoSourceLock = "Les notes ont \u00E9t\u00E9 envoy\u00E9es, mais \u00AB Envoy\u00E9e \u00E0 \u00BB n'a pas pu \u00EAtre ajout\u00E9 dans {0} (vous y \u00EAtes peut-\u00EAtre observateur, ou ses notes sont en cours d'utilisation).";
            s.ResMarkFailed = "\u00AB Envoy\u00E9e \u00E0 \u00BB non ajout\u00E9 \u00E0 {0} : {1}";
            s.OpenProjectFirst = "Il n'y a aucun projet de notes de consultant \u00E0 partir duquel envoyer des notes. Ouvrez le projet qui contient les notes, puis choisissez de nouveau cette commande.";
            s.Error = "Envoyer des notes \u00E0 un projet s'est arr\u00EAt\u00E9 \u00E0 cause d'une erreur :\n{0}";
            return s;
        }
    }
}
