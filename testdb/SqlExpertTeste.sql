IF DB_ID('SqlExpertTeste') IS NULL CREATE DATABASE SqlExpertTeste;
GO
USE SqlExpertTeste;
GO
CREATE TABLE dbo.Clientes (
  ClienteId INT IDENTITY(1,1) CONSTRAINT PK_Clientes PRIMARY KEY,
  Nome NVARCHAR(120) NOT NULL,
  Email NVARCHAR(150) NULL CONSTRAINT UQ_Clientes_Email UNIQUE,
  Cidade NVARCHAR(80) NULL,
  CriadoEm DATETIME2 NOT NULL CONSTRAINT DF_Clientes_CriadoEm DEFAULT SYSDATETIME()
);
CREATE TABLE dbo.Categorias (
  CategoriaId INT IDENTITY(1,1) CONSTRAINT PK_Categorias PRIMARY KEY,
  Nome NVARCHAR(60) NOT NULL
);
CREATE TABLE dbo.Produtos (
  ProdutoId INT IDENTITY(1,1) CONSTRAINT PK_Produtos PRIMARY KEY,
  CategoriaId INT NOT NULL CONSTRAINT FK_Produtos_Categorias REFERENCES dbo.Categorias(CategoriaId),
  Nome NVARCHAR(120) NOT NULL,
  Preco DECIMAL(10,2) NOT NULL CONSTRAINT CK_Produtos_Preco CHECK (Preco >= 0),
  Estoque INT NOT NULL CONSTRAINT DF_Produtos_Estoque DEFAULT 0
);
CREATE TABLE dbo.Pedidos (
  PedidoId INT IDENTITY(1,1) CONSTRAINT PK_Pedidos PRIMARY KEY,
  ClienteId INT NOT NULL CONSTRAINT FK_Pedidos_Clientes REFERENCES dbo.Clientes(ClienteId),
  DataPedido DATETIME2 NOT NULL CONSTRAINT DF_Pedidos_Data DEFAULT SYSDATETIME(),
  Status VARCHAR(20) NOT NULL CONSTRAINT DF_Pedidos_Status DEFAULT 'ABERTO'
);
CREATE TABLE dbo.ItensPedido (
  PedidoId INT NOT NULL CONSTRAINT FK_Itens_Pedidos REFERENCES dbo.Pedidos(PedidoId),
  ProdutoId INT NOT NULL CONSTRAINT FK_Itens_Produtos REFERENCES dbo.Produtos(ProdutoId),
  Quantidade INT NOT NULL CONSTRAINT CK_Itens_Qtd CHECK (Quantidade > 0),
  PrecoUnitario DECIMAL(10,2) NOT NULL,
  CONSTRAINT PK_ItensPedido PRIMARY KEY (PedidoId, ProdutoId)
);
CREATE INDEX IX_Pedidos_ClienteId ON dbo.Pedidos(ClienteId);
CREATE INDEX IX_Produtos_CategoriaId ON dbo.Produtos(CategoriaId);
GO
CREATE SEQUENCE dbo.SeqNotaFiscal AS INT START WITH 1000 INCREMENT BY 1;
GO
CREATE VIEW dbo.vwPedidosResumo AS
SELECT p.PedidoId, c.Nome AS Cliente, p.DataPedido, p.Status,
       SUM(i.Quantidade * i.PrecoUnitario) AS Total
FROM dbo.Pedidos p
JOIN dbo.Clientes c ON c.ClienteId = p.ClienteId
JOIN dbo.ItensPedido i ON i.PedidoId = p.PedidoId
GROUP BY p.PedidoId, c.Nome, p.DataPedido, p.Status;
GO
CREATE FUNCTION dbo.fnTotalPedido (@PedidoId INT) RETURNS DECIMAL(12,2) AS
BEGIN
  RETURN (SELECT SUM(Quantidade * PrecoUnitario) FROM dbo.ItensPedido WHERE PedidoId = @PedidoId);
END;
GO
CREATE PROCEDURE dbo.spPedidosPorCliente @ClienteId INT AS
BEGIN
  SET NOCOUNT ON;
  SELECT * FROM dbo.vwPedidosResumo WHERE Cliente = (SELECT Nome FROM dbo.Clientes WHERE ClienteId = @ClienteId);
END;
GO
CREATE TRIGGER dbo.trItensBaixaEstoque ON dbo.ItensPedido AFTER INSERT AS
BEGIN
  SET NOCOUNT ON;
  UPDATE pr SET Estoque = pr.Estoque - i.Quantidade
  FROM dbo.Produtos pr JOIN inserted i ON i.ProdutoId = pr.ProdutoId;
END;
GO
INSERT INTO dbo.Clientes (Nome, Email, Cidade) VALUES
 (N'Ana Souza','ana@exemplo.com',N'São Paulo'),
 (N'Bruno Lima','bruno@exemplo.com',N'Campinas'),
 (N'Carla Mendes','carla@exemplo.com',N'Rio de Janeiro'),
 (N'Diego Rocha',NULL,N'Curitiba');
INSERT INTO dbo.Categorias (Nome) VALUES (N'Informática'),(N'Escritório'),(N'Games');
INSERT INTO dbo.Produtos (CategoriaId, Nome, Preco, Estoque) VALUES
 (1,N'Teclado mecânico',299.90,50),(1,N'Mouse sem fio',129.90,80),
 (2,N'Caderno A4',24.50,200),(2,N'Caneta azul (cx 50)',39.00,120),
 (3,N'Controle USB',189.00,30);
INSERT INTO dbo.Pedidos (ClienteId, Status) VALUES (1,'PAGO'),(2,'ABERTO'),(1,'ENVIADO'),(3,'ABERTO');
INSERT INTO dbo.ItensPedido (PedidoId, ProdutoId, Quantidade, PrecoUnitario) VALUES
 (1,1,1,299.90),(1,2,2,129.90),(2,3,10,24.50),(3,5,1,189.00),(4,4,3,39.00),(4,2,1,129.90);
GO
