internal static class ApiProblems
{
    public static string Title(int status) => status switch
    {
        400 => "Dados inválidos",
        401 => "Autenticação necessária",
        403 => "Acesso negado",
        404 => "Recurso não encontrado",
        405 => "Método não permitido",
        409 => "Conflito na operação",
        413 => "Limite de tamanho excedido",
        415 => "Formato não suportado",
        >= 500 => "Erro no servidor",
        _ => "Não foi possível concluir a operação"
    };

    public static string Detail(int status) => status switch
    {
        400 => "Confira os dados enviados. O corpo e os campos da requisição devem seguir o formato esperado.",
        404 => "O recurso solicitado não foi encontrado.",
        405 => "O método HTTP utilizado não é permitido para este recurso.",
        413 => "A requisição excede o tamanho permitido.",
        415 => "O formato do conteúdo enviado não é suportado por este recurso.",
        >= 500 => "Não foi possível concluir a operação devido a um erro no servidor. Tente novamente.",
        _ => "Não foi possível concluir a operação. Confira os dados e tente novamente."
    };
}
