﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using enki.storage.Interface;

namespace enki.storage.Model
{
    public class AwsS3Storage : BaseStorage
    {
        private IAmazonS3 _client { get; set; }
        public static bool IsAmazonS3Config(IStorageServerConfig config) => config.EndPoint.ToUpper().Trim() == "S3.AMAZONAWS.COM";
        public bool IsAmazonS3Config() => IsAmazonS3Config(ServerConfig);
        public bool UseRegion => !string.IsNullOrWhiteSpace(ServerConfig.Region);

        public AwsS3Storage(IStorageServerConfig config) : base(config)
        {
            if (!IsAmazonS3Config()) throw new ArgumentException("Endpoint is not valid AWS S3.");
        }

        /// <summary>
        /// Efetua a conexão com o servidor Minio/S3 a partir dos dados do construtor.
        /// </summary>
        public override void Connect()
        {
            if (_client != null) return;
            var credentials = new BasicAWSCredentials(ServerConfig.AccessKey, ServerConfig.SecretKey);
            _client = new AmazonS3Client(credentials, RegionEndpoint.GetBySystemName(ServerConfig.Region));
        }

        /// <summary>
        /// Valida se um balde existe de forma assincrona.
        /// </summary>
        /// <param name="bucketName">Nome da carteira a ser pesquisada.</param>
        /// <returns>Tarefa indicando sucesso ou falha ao terminar.</returns>
        public override async Task<bool> BucketExistsAsync(string bucketName)
        {
            ValidateInstance();
            return await _client.DoesS3BucketExistAsync(bucketName).ConfigureAwait(false);
            //var buckets = await _client.ListBucketsAsync().ConfigureAwait(false);
            //return buckets.Buckets.Exists(b => b.BucketName == bucketName);
        }

        /// <summary>
        /// Cria um balde de forma assincrona no servidor.
        /// </summary>
        /// <param name="bucketName">Nome da carteira a ser criada.</param>
        public override async Task MakeBucketAsync(string bucketName)
        {
            ValidateInstance();
            await _client.PutBucketAsync(bucketName).ConfigureAwait(false);
        }

        /// <summary>
        /// Ativa regra de CORS no Bucket para permitir acesso externo via javascript.
        /// </summary>
        /// <param name="bucketName">Nome do Bucket para adicionar a regra de CORS</param>
        /// <param name="allowedOrigin">Origigem a ser ativada.</param>
        /// <returns></returns>
        public override async Task SetCorsToBucketAsync(string bucketName, string allowedOrigin)
        {
            ValidateInstance();

            // Remove configuração CORS
            var requestDelete = new DeleteCORSConfigurationRequest
            {
                BucketName = bucketName
            };
            await _client.DeleteCORSConfigurationAsync(requestDelete);

            // Cria nova configuração CORS
            var configuration = new CORSConfiguration
            {
                Rules = new List<CORSRule>
                {
                      new CORSRule
                      {
                            Id = "enContactPutByJavascriptRule",
                            AllowedMethods = new List<string> { "PUT" },
                            AllowedHeaders = new List<string> { "*" },
                            AllowedOrigins = new List<string> { allowedOrigin ?? "*" }
                      },
                }
            };
            var requestCreate = new PutCORSConfigurationRequest
            {
                BucketName = bucketName,
                Configuration = configuration
            };
            await _client.PutCORSConfigurationAsync(requestCreate).ConfigureAwait(false);
        }

        /// <summary>
        /// Este método é específico para AWS e recupera a configuração do CORS existente.
        /// </summary>
        /// <returns>Recupera a configuração existente do CORS</returns>
        public async Task<CORSConfiguration> RetrieveCORSConfigurationAsync(string bucketName)
        {
            var request = new GetCORSConfigurationRequest
            {
                BucketName = bucketName

            };
            var response = await _client.GetCORSConfigurationAsync(request);
            var configuration = response.Configuration;
            return configuration;
        }

        /// <summary>
        /// Exclui um balde no servidor
        /// </summary>
        /// <param name="bucketName">Nome do balde a ser removido</param>
        public override async Task RemoveBucketAsync(string bucketName)
        {
            ValidateInstance();
            await _client.DeleteBucketAsync(bucketName).ConfigureAwait(false);
        }

        /// <summary>
        /// Efetua a criação de uma URL temporária para upload de anexo sem depender de autenticação.
        /// Util para performar os uploads tanto de anexos como de imagens no corpo efetuadas pela plataforma.
        /// </summary>
        /// <param name="bucketName">Bucket onde será inserido o registro.</param>
        /// <param name="objectName">Nome/Caminho do objeto a ser inserido.</param>
        /// <param name="expiresInt">Tempo em segundos no qual a url será valida para o Upload.</param>
        /// <returns></returns>
        public override async Task<string> PresignedPutObjectAsync(string bucketName, string objectName, int expiresInt)
        {
            ValidateInstance();

            return await Task.Run(() =>
            {
                var request = new GetPreSignedUrlRequest
                {
                    BucketName = bucketName,
                    Key = objectName,
                    Verb = HttpVerb.PUT,
                    Expires = DateTime.UtcNow.AddSeconds(expiresInt)
                };
                return _client.GetPreSignedURL(request);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Efetua o upload de um arquivo a partir do servidor.
        /// </summary>
        /// <param name="bucketName">Nome do balde</param>
        /// <param name="objectName">Nome do objeto</param>
        /// <param name="filePath">Caminho do arquivo no servidor</param>
        /// <param name="contentType">Tipo do conteúdo do arquivo</param>
        /// <returns>Tarefa em execução.</returns>
        public override async Task PutObjectAsync(string bucketName, string objectName, string filePath, string contentType)
        {
            ValidateInstance();
            var request = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = objectName,
                FilePath = filePath,
                ContentType = contentType
            };
            await _client.PutObjectAsync(request).ConfigureAwait(false);
        }

        /// <summary>
        /// Efetua o upload de um arquivo a partir de um Stream em memória.
        /// </summary>
        /// <param name="bucketName">Nome do balde</param>
        /// <param name="objectName">Nome do objeto</param>
        /// <param name="data">Stream com conteúdo</param>
        /// <param name="size">Tamanho do conteúdo</param>
        /// <param name="contentType">Tipo do conteudo</param>
        /// <returns>Tarefa em execução.</returns>
        public override async Task PutObjectAsync(string bucketName, string objectName, Stream data, long size, string contentType)
        {
            ValidateInstance();
            var request = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = objectName,
                InputStream = data,
                ContentType = contentType
            };
            await _client.PutObjectAsync(request).ConfigureAwait(false);
        }

        /// <summary>
        /// Remove um arquivo contido num balde
        /// </summary>
        /// <param name="bucketName">Nome do balde onde o arquivo se encontra.</param>
        /// <param name="objectName">Nome do objeto a ser removido.</param>
        /// <returns>Tarefa em execução.</returns>
        public override async Task RemoveObjectAsync(string bucketName, string objectName)
        {
            ValidateInstance();
            await _client.DeleteObjectAsync(bucketName, objectName).ConfigureAwait(false);
        }

        ///// <summary>
        ///// Valida se um objeto existe ou não no balde.
        ///// NOTA: Este método é mais rápido segundo informações, porém durante testes, a validação de
        /////       um objeto recém criado apresentava resultado FALSE.
        ///// </summary>
        ///// <param name="bucketName">Nome do balde</param>
        ///// <param name="objectName">Nome do objeto</param>
        ///// <returns>True se existe e False se não existe.</returns>
        //public async Task<bool> ObjectExistByMetadataAsync(string bucketName, string objectName)
        //{
        //    ValidateInstance();
        //    try
        //    {
        //        var metadata = await _client.GetObjectMetadataAsync(bucketName, objectName).ConfigureAwait(false);
        //        if (metadata.HttpStatusCode == System.Net.HttpStatusCode.Found) return true;
        //        return false;
        //    }
        //    catch (AmazonS3Exception ex)
        //    {
        //        if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        //            return false;

        //        //status wasn't not found, so throw the exception
        //        throw;
        //    }
        //}

        /// <summary>
        /// Valida se um objeto existe ou não no balde.
        /// </summary>
        /// <param name="bucketName">Nome do balde</param>
        /// <param name="objectName">Nome do objeto</param>
        /// <returns>True se existe e False se não existe.</returns>
        public override async Task<bool> ObjectExistAsync(string bucketName, string objectName)
        {
            var request = new ListObjectsV2Request
            {
                BucketName = bucketName,
                Prefix = objectName,
                MaxKeys = 1
            };

            var response = await _client.ListObjectsV2Async(request).ConfigureAwait(false);

            if (response.S3Objects.Count == 0)
                return false;

            return true;
        }

        /// <summary>
        /// Recupera um objeto do balde.
        /// </summary>
        /// <param name="bucketName">Nome do balde.</param>
        /// <param name="objectName">Nome do objeto a ser recuperado.</param>
        /// <param name="action">Função de callback com a Stream recuperada do servidor.</param>
        public override async Task GetObjectAsync(string bucketName, string objectName, Action<Stream> action)
        {
            ValidateInstance();
            var result = await _client.GetObjectAsync(bucketName, objectName).ConfigureAwait(false);
            action(result.ResponseStream);
        }

        /// <summary>
        /// Efetua a copia de um objeto no servidor, evitando a necessidade de efetuar um upload.
        /// </summary>
        /// <param name="bucketName">Nome do balde de origem da copia.</param>
        /// <param name="objectName">Nome do objecto de origem da copia.</param>
        /// <param name="destBucketName">Balde de destino</param>
        /// <param name="destObjectName">Objeto de destino</param>
        /// <returns>Tarefa sendo executada.</returns>
        public override async Task CopyObjectAsync(string bucketName, string objectName, string destBucketName, string destObjectName)
        {
            ValidateInstance();
            await _client.CopyObjectAsync(bucketName, objectName, destBucketName, destObjectName).ConfigureAwait(false);
        }

        /// <summary>
        /// Obtém uma url de acesso temporário ao anexo.
        /// </summary>
        /// <param name="bucketName">Nome do balde de origem da copia.</param>
        /// <param name="objectName">Nome do objecto de origem da copia.</param>
        /// <param name="expiresInt">Tempo de expiração em segundos.</param>
        /// <param name="reqParams">Parametros adicionais do Header a serem utilizados. Suporta os Headers: response-expires, response-content-type, response-cache-control, response-content-disposition</param>
        /// <returns>Url para obtenção do arquivo.</returns>
        public override async Task<string> PresignedGetObjectAsync(string bucketName, string objectName, int expiresInt, Dictionary<string, string> reqParams = null)
        {
            ValidateInstance();

            return await Task.Run(() =>
            {
                var request = new GetPreSignedUrlRequest
                {
                    BucketName = bucketName,
                    Key = objectName,
                    Verb = HttpVerb.GET,
                    Expires = DateTime.UtcNow.AddSeconds(expiresInt)
                };
                if (reqParams != null)
                {
                    if (reqParams.ContainsKey("response-expires")) request.ResponseHeaderOverrides.Expires = reqParams["response-expires"];
                    if (reqParams.ContainsKey("response-content-type")) request.ResponseHeaderOverrides.Expires = reqParams["response-content-type"];
                    if (reqParams.ContainsKey("response-cache-control")) request.ResponseHeaderOverrides.Expires = reqParams["response-cache-control"];
                    if (reqParams.ContainsKey("response-content-disposition")) request.ResponseHeaderOverrides.Expires = reqParams["response-content-disposition"];
                }
                return _client.GetPreSignedURL(request);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Efetua a validação se o usuário efetuou a conexão antes de executar as ações.
        /// </summary>
        private void ValidateInstance()
        {
            if (_client == null)
                throw new ObjectDisposedException("Não foi efetuada conexão com o servidor. Utilize a função Connect() antes de chamar as ações.");
        }
    }
}
